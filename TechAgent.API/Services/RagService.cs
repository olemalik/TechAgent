using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using OilGasAI.API.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace OilGasAI.API.Services;

/// <summary>
/// Full RAG pipeline:
///   1. Embed user question (nomic-embed-text → 768-dim vector)
///   2. Domain guard: keyword + cosine similarity to O&G centroid (reuses step-1 embedding)
///   3. Vector search Qdrant → top-4 child chunk IDs
///   4. Load PARENT chunks from PostgreSQL (600 words, richer LLM context)
///   5. Build enriched prompt: [document context] + [last 8 conversation turns] + [question]
///   6. Call Ollama oilgas-assistant (stream:false)
///   7. Cache answer by SHA-256(prompt) via HybridCache (stampede-safe)
///   8. Persist to ChatHistory in PostgreSQL
/// </summary>
public class RagService : IRagService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IEmbeddingService _embedding;
    private readonly IQdrantService _qdrant;
    private readonly IDomainGuardService _guard;
    private readonly HttpClient _http;
    private readonly HybridCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<RagService> _log;

    private const int MaxHistoryTurns = 8;
    private const int TopK = 4;

    public RagService(
        IDbContextFactory<AppDbContext> dbFactory,
        IEmbeddingService embedding,
        IQdrantService qdrant,
        IDomainGuardService guard,
        IHttpClientFactory httpFactory,
        HybridCache cache,
        IConfiguration config,
        ILogger<RagService> log)
    {
        _dbFactory = dbFactory; _embedding = embedding; _qdrant = qdrant;
        _guard = guard; _cache = cache; _config = config; _log = log;
        _http = httpFactory.CreateClient("ollama-generate");
    }

    public async Task<RagChatResponse> AskAsync(RagChatRequest request, CancellationToken ct = default)
    {
        var sessionId = request.SessionId ?? Guid.NewGuid();

        // Step 1: Embed question — uses "search_query:" prefix (required for nomic-embed-text)
        var queryVec = await _embedding.EmbedAsync($"search_query: {request.Message}", ct);

        // Step 2: Domain guard (REUSES embedding — no extra Ollama call)
        if (!await _guard.IsAllowedAsync(request.Message, queryVec, ct))
        {
            const string refusal = "I can only assist with Oil & Gas industry topics. " +
                "Please ask about drilling, production, HSE, reservoir engineering, pipelines, or refining.";
            await PersistAsync(sessionId, request.Message, refusal, refused: true, ct);
            return new RagChatResponse { SessionId = sessionId, Reply = refusal, IsSuccess = true, WasRefused = true };
        }

        // Step 3: Vector search — find top-4 relevant chunks
        var hits = await _qdrant.SearchAsync(queryVec, TopK, ct);
        var chunkIds = hits.Select(h => h.ChunkId).ToList();

        // Step 4: Load parent chunks from PostgreSQL
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var chunks = chunkIds.Count > 0
            ? await db.DocumentChunks
                .Where(c => chunkIds.Contains(c.Id))
                .Select(c => new { c.ParentText, c.Document.FileName })
                .AsNoTracking()
                .ToListAsync(ct)
            : [];

        // Step 5: Load last 8 conversation turns
        var history = await db.ChatHistory
            .Where(h => h.SessionId == sessionId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(MaxHistoryTurns)
            .OrderBy(h => h.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        // Step 6: Build enriched prompt and call Ollama
        var prompt = BuildPrompt(request.Message, chunks.Select(c => (c.ParentText, c.FileName)).ToList(), history);

        var answer = await _cache.GetOrCreateAsync(
            $"rag:{Hash(prompt)}",
            factory: async innerCt => await CallOllamaAsync(prompt, innerCt),
            options: new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(30) },
            cancellationToken: ct);

        var sources = chunks.Select(c => c.FileName).Distinct().ToList();
        await PersistAsync(sessionId, request.Message, answer, refused: false, ct);

        return new RagChatResponse
        {
            SessionId = sessionId,
            Reply = answer,
            IsSuccess = true,
            WasRefused = false,
            Sources = sources
        };
    }

    private async Task<string> CallOllamaAsync(string prompt, CancellationToken ct)
    {
        var ollamaUrl = _config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        var model = _config["Ollama:ONGModelName"] ?? "oilgas-assistant";

        using var response = await _http.PostAsJsonAsync(
            $"{ollamaUrl}/api/generate",
            new { model, prompt, stream = false },
            ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(ct)
                   ?? throw new InvalidOperationException("Empty Ollama response.");
        return body.response.Trim();
    }

    private static string BuildPrompt(
        string question,
        IReadOnlyList<(string ParentText, string FileName)> chunks,
        IReadOnlyList<ChatSessionHistory> history)
    {
        var sb = new StringBuilder();

        if (history.Count > 0)
        {
            sb.AppendLine("CONVERSATION HISTORY:");
            foreach (var h in history)
                sb.AppendLine($"{h.Role.ToUpper()}: {h.Message}");
            sb.AppendLine();
        }

        if (chunks.Count > 0)
        {
            sb.AppendLine("[CONTEXT START]");
            foreach (var (text, file) in chunks)
            {
                sb.AppendLine($"Source: {file}");
                sb.AppendLine(text);
                sb.AppendLine("---");
            }
            sb.AppendLine("[CONTEXT END]");
            sb.AppendLine("Using the context above, answer the Oil & Gas question accurately.");
            sb.AppendLine("If the context does not contain the answer, say so clearly. Never fabricate.");
        }
        else
        {
            sb.AppendLine("No document context available. Answer from Oil & Gas training knowledge only.");
        }

        sb.AppendLine($"\nQUESTION: {question}");
        sb.AppendLine("ANSWER:");
        return sb.ToString();
    }

    private async Task PersistAsync(Guid sessionId, string question, string answer, bool refused, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ChatHistory.AddRange(
            new ChatSessionHistory { SessionId = sessionId, Role = "user", Message = question },
            new ChatSessionHistory { SessionId = sessionId, Role = "assistant", Message = answer, WasRefused = refused });
        await db.SaveChangesAsync(ct);
    }

    private static string Hash(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}