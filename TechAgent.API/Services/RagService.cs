using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using OilGasAI.API.Interfaces;
using OilGasAI.API.Models;
using System.Security.Cryptography;
using System.Text;

namespace OilGasAI.API.Services;

/// <summary>
/// Full RAG pipeline:
///   1. Embed user question via IAIService
///   2. Domain guard: keyword + cosine similarity to O&G centroid
///   3. Vector search Qdrant → top-4 child chunk IDs
///   4. Load PARENT chunks from PostgreSQL (richer LLM context)
///   5. Build enriched prompt: [context] + [last 8 turns] + [question]
///   6. Call AI via IAIService (Ollama / OpenAI / Claude)
///   7. Cache answer by SHA-256(question) via HybridCache (stampede-safe)
///   8. Persist to ChatHistory in PostgreSQL
/// </summary>
public class RagService : IRagService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IQdrantService _qdrant;
    private readonly IDomainGuardService _guard;
    private readonly HybridCache _cache;
    private readonly IAIService _aiService;
    private readonly ILogger<RagService> _log;

    private const int MaxHistoryTurns = 8;
    private const int TopK = 4;

    public RagService(
        IDbContextFactory<AppDbContext> dbFactory,
        IQdrantService qdrant,
        IDomainGuardService guard,
        HybridCache cache,
        IAIService aiService,
        ILogger<RagService> log)
    {
        _dbFactory = dbFactory;
        _qdrant = qdrant;
        _guard = guard;
        _cache = cache;
        _aiService = aiService;
        _log = log;
    }

    public async Task<RagChatResponse> AskAsync(RagChatRequest request, CancellationToken ct = default)
    {
        var sessionId = request.SessionId ?? Guid.NewGuid();

        // Step 1: Embed question via IAIService
        var queryVec = await _aiService.GetEmbeddingAsync(request.Message, ct);
        _log.LogInformation("Embedding generated for query: {Query}", request.Message);

        // Step 2: Domain guard (reuses embedding — no extra AI call)
        if (!await _guard.IsAllowedAsync(request.Message, queryVec, ct))
        {
            const string refusal =
                "Thank you for your question. I'm a specialist AI assistant focused exclusively on the Oil & Gas industry, " +
                "so I'm not able to help with that particular topic.\n\n" +
                "I'm well-equipped to assist you with:\n" +
                "• Drilling & well operations\n" +
                "• Reservoir engineering & production optimisation\n" +
                "• HSE, safety procedures & incident management\n" +
                "• Pipeline integrity & flow assurance\n" +
                "• Refining, processing & LNG operations\n" +
                "• Upstream, midstream & downstream topics\n\n" +
                "Feel free to ask me anything within these areas — I'm here to help!";

            await PersistAsync(sessionId, request.Message, refusal, refused: true, ct);

            return new RagChatResponse
            {
                SessionId = sessionId,
                Reply = refusal,
                IsSuccess = true,
                WasRefused = true
            };
        }

        // Step 3: Vector search — find top-4 relevant chunks
        var hits = await _qdrant.SearchAsync(queryVec, TopK, ct);
        var chunkIds = hits.Select(h => h.ChunkId).ToList();
        _log.LogInformation("Found {Count} chunks for query: {Query}", chunkIds.Count, request.Message);

        // Step 4: Load parent chunks + conversation history from PostgreSQL
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var chunks = chunkIds.Count > 0
            ? await db.DocumentChunks
                .Where(c => chunkIds.Contains(c.Id))
                .Select(c => new { c.ParentText, c.Document.FileName })
                .AsNoTracking()
                .ToListAsync(ct)
            : [];

        var history = await db.ChatHistory
            .Where(h => h.SessionId == sessionId && !h.IsDeleted)
            .OrderByDescending(h => h.CreatedAt)
            .Take(MaxHistoryTurns)
            .OrderBy(h => h.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        // Step 5: Build enriched prompt
        var prompt = BuildPrompt(
            request.Message,
            chunks.Select(c => (c.ParentText, c.FileName)).ToList(),
            history);

        // Step 6 + 7: Get answer — skip cache for conversational turns
        var answer = history.Count == 0
            ? await _cache.GetOrCreateAsync(
                $"rag:{Hash(request.Message)}",
                factory: async innerCt => await _aiService.ChatAsync(prompt, innerCt),
                options: new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(30) },
                cancellationToken: ct)
            : await _aiService.ChatAsync(prompt, ct);

        // Step 8: Persist to PostgreSQL
        var sources = chunks.Select(c => c.FileName).Distinct().ToList();
        await PersistAsync(sessionId, request.Message, answer, refused: false, ct);

        _log.LogInformation(
            "RAG answer generated. SessionId: {SessionId}, Sources: {Sources}",
            sessionId, string.Join(", ", sources));

        return new RagChatResponse
        {
            SessionId = sessionId,
            Reply = answer,
            IsSuccess = true,
            WasRefused = false,
            Sources = sources
        };
    }

    public async IAsyncEnumerable<RagStreamChunk> StreamAsync(
        RagChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sessionId = request.SessionId ?? Guid.NewGuid();

        var queryVec = await _aiService.GetEmbeddingAsync(request.Message, ct);

        if (!await _guard.IsAllowedAsync(request.Message, queryVec, ct))
        {
            const string refusal =
                "Thank you for your question. I'm a specialist AI assistant focused exclusively on the Oil & Gas industry, " +
                "so I'm not able to help with that particular topic.\n\n" +
                "I'm well-equipped to assist you with:\n" +
                "• Drilling & well operations\n" +
                "• Reservoir engineering & production optimisation\n" +
                "• HSE, safety procedures & incident management\n" +
                "• Pipeline integrity & flow assurance\n" +
                "• Refining, processing & LNG operations\n" +
                "• Upstream, midstream & downstream topics\n\n" +
                "Feel free to ask me anything within these areas — I'm happy to help!";

            await PersistAsync(sessionId, request.Message, refusal, refused: true, ct);
            yield return new RagStreamChunk { Type = "token", Value = refusal };
            yield return new RagStreamChunk { Type = "done", SessionId = sessionId, WasRefused = true };
            yield break;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var hits = await _qdrant.SearchAsync(queryVec, TopK, ct);
        var chunkIds = hits.Select(h => h.ChunkId).ToList();

        var chunks = chunkIds.Count > 0
            ? await db.DocumentChunks
                .Where(c => chunkIds.Contains(c.Id))
                .Select(c => new { c.ParentText, c.Document.FileName })
                .AsNoTracking()
                .ToListAsync(ct)
            : [];

        var history = await db.ChatHistory
            .Where(h => h.SessionId == sessionId && !h.IsDeleted)
            .OrderByDescending(h => h.CreatedAt)
            .Take(MaxHistoryTurns)
            .OrderBy(h => h.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        var prompt = BuildPrompt(
            request.Message,
            chunks.Select(c => (c.ParentText, c.FileName)).ToList(),
            history);

        var fullText = new StringBuilder();
        await foreach (var token in _aiService.StreamAsync(prompt, ct))
        {
            fullText.Append(token);
            yield return new RagStreamChunk { Type = "token", Value = token };
        }

        await PersistAsync(sessionId, request.Message, fullText.ToString(), refused: false, ct);
        yield return new RagStreamChunk { Type = "done", SessionId = sessionId, WasRefused = false };
    }

    // ── Prompt Builder ────────────────────────────────────────────────────────

    private static string BuildPrompt(
        string question,
        IReadOnlyList<(string ParentText, string FileName)> chunks,
        IReadOnlyList<ChatSessionHistory> history)
    {
        var sb = new StringBuilder();

        // 1. Document context first (most important for grounding)
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

        // 2. Conversation history second
        if (history.Count > 0)
        {
            sb.AppendLine("\nCONVERSATION HISTORY:");
            foreach (var h in history)
                sb.AppendLine($"{h.Role.ToUpper()}: {h.Message}");
        }

        // 3. Question last
        sb.AppendLine($"\nQUESTION: {question}");
        sb.AppendLine("ANSWER:");

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task PersistAsync(
        Guid sessionId,
        string question,
        string answer,
        bool refused,
        CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.ChatHistory.AddRange(
                new ChatSessionHistory
                {
                    SessionId = sessionId,
                    Role = "user",
                    Message = question
                },
                new ChatSessionHistory
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Message = answer,
                    WasRefused = refused
                });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Log but don't rethrow — user still gets their answer
            _log.LogError(ex, "Failed to persist chat history for session {SessionId}", sessionId);
        }
    }

    private static string Hash(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}