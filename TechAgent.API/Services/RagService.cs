using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
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
    private readonly McpToolRegistry _mcpTools;
    private readonly ILogger<RagService> _log;

    private const int MaxHistoryTurns = 8;
    private const int TopK = 4;

    private readonly float _minSimilarityScore;

    public RagService(
        IDbContextFactory<AppDbContext> dbFactory,
        IQdrantService qdrant,
        IDomainGuardService guard,
        HybridCache cache,
        IAIService aiService,
        McpToolRegistry mcpTools,
        IConfiguration config,
        ILogger<RagService> log)
    {
        _dbFactory = dbFactory;
        _qdrant = qdrant;
        _guard = guard;
        _cache = cache;
        _aiService = aiService;
        _mcpTools = mcpTools;
        _log = log;
        _minSimilarityScore = config.GetValue<float>("Qdrant:MinSimilarityScore", 0.45f);
        _supportsFunctionCalling = config.GetValue<bool>("AI:SupportsFunctionCalling", false);
    }

    private readonly bool _supportsFunctionCalling;

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

        // Step 3: Vector search — find top-4 relevant chunks, drop low-confidence hits
        var hits = await _qdrant.SearchAsync(queryVec, TopK, ct);
        var relevantHits = hits.Where(h => h.Score >= _minSimilarityScore).ToList();
        var chunkIds = relevantHits.Select(h => h.ChunkId).ToList();
        _log.LogInformation(
            "Qdrant returned {Total} hits; {Kept} above score threshold {Threshold} for query: {Query}",
            hits.Count, chunkIds.Count, _minSimilarityScore, request.Message);

        // Step 4: Load parent chunks + conversation history + golden examples from PostgreSQL
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Fetch chunks then re-order by Qdrant relevance score — DB order is arbitrary,
        // and LLMs weight earlier context more heavily.
        var rawChunks = chunkIds.Count > 0
            ? await db.DocumentChunks
                .Where(c => chunkIds.Contains(c.Id))
                .Select(c => new { c.Id, c.ParentText, c.Document.FileName })
                .AsNoTracking()
                .ToListAsync(ct)
            : [];

        var chunkLookup = rawChunks.ToDictionary(c => c.Id);
        var chunks = relevantHits
            .Where(h => chunkLookup.ContainsKey(h.ChunkId))
            .Select(h => chunkLookup[h.ChunkId])
            .ToList();

        var history = await db.ChatHistory
            .Where(h => h.SessionId == sessionId && !h.IsDeleted)
            .OrderByDescending(h => h.CreatedAt)
            .Take(MaxHistoryTurns)
            .OrderBy(h => h.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        // Fetch up to 3 golden Q&A pairs (highest-rated, expert-verified answers)
        var goldenPairs = await db.ChatHistory
            .Where(h => h.IsGolden && h.Role == "assistant" && !h.IsDeleted)
            .OrderByDescending(h => h.FeedbackScore)
            .Take(3)
            .AsNoTracking()
            .ToListAsync(ct);

        // Step 5 + 6: Build messages and attach MCP tools.
        // System message carries RAG context + instructions; User message carries only the question.
        // Splitting them lets UseFunctionInvocation() middleware cleanly insert tool call/result
        // turns between system context and the question without corrupting the prompt format.
        var tools = _supportsFunctionCalling ? _mcpTools.GetTools() : [];
        var options = tools.Count > 0
            ? new ChatOptions { Tools = [..tools] }
            : null;
        var messages = BuildMessages(
            request.Message,
            request.AttachmentName,
            chunks.Select(c => (c.ParentText, c.FileName)).ToList(),
            history,
            goldenPairs);

        var answer = history.Count == 0
            ? await _cache.GetOrCreateAsync(
                $"rag:{Hash(request.Message)}",
                factory: async innerCt => await _aiService.ChatAsync(messages, options, innerCt),
                options: new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(30) },
                cancellationToken: ct)
            : await _aiService.ChatAsync(messages, options, ct);

        // Step 8: Persist to PostgreSQL (include attachment metadata if present)
        var sources = chunks.Select(c => c.FileName).Distinct().ToList();
        var assistantId = await PersistAsync(sessionId, request.Message, answer, refused: false, ct,
            request.AttachmentName, request.AttachmentUrl, request.AttachmentContentType);

        _log.LogInformation(
            "RAG answer generated. SessionId: {SessionId}, Sources: {Sources}",
            sessionId, string.Join(", ", sources));

        return new RagChatResponse
        {
            SessionId = sessionId,
            Reply = answer,
            IsSuccess = true,
            WasRefused = false,
            Sources = sources,
            AssistantMessageId = assistantId
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
        var relevantHits = hits.Where(h => h.Score >= _minSimilarityScore).ToList();
        var chunkIds = relevantHits.Select(h => h.ChunkId).ToList();
        _log.LogInformation(
            "Qdrant returned {Total} hits; {Kept} above threshold {Threshold}",
            hits.Count, chunkIds.Count, _minSimilarityScore);

        var rawChunks = chunkIds.Count > 0
            ? await db.DocumentChunks
                .Where(c => chunkIds.Contains(c.Id))
                .Select(c => new { c.Id, c.ParentText, c.Document.FileName })
                .AsNoTracking()
                .ToListAsync(ct)
            : [];

        var chunkLookup = rawChunks.ToDictionary(c => c.Id);
        var chunks = relevantHits
            .Where(h => chunkLookup.ContainsKey(h.ChunkId))
            .Select(h => chunkLookup[h.ChunkId])
            .ToList();

        var history = await db.ChatHistory
            .Where(h => h.SessionId == sessionId && !h.IsDeleted)
            .OrderByDescending(h => h.CreatedAt)
            .Take(MaxHistoryTurns)
            .OrderBy(h => h.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        var goldenPairs = await db.ChatHistory
            .Where(h => h.IsGolden && h.Role == "assistant" && !h.IsDeleted)
            .OrderByDescending(h => h.FeedbackScore)
            .Take(3)
            .AsNoTracking()
            .ToListAsync(ct);

        var streamTools = _supportsFunctionCalling ? _mcpTools.GetTools() : [];
        var streamOptions = streamTools.Count > 0
            ? new ChatOptions { Tools = [..streamTools] }
            : null;
        var streamMessages = BuildMessages(
            request.Message,
            request.AttachmentName,
            chunks.Select(c => (c.ParentText, c.FileName)).ToList(),
            history,
            goldenPairs);

        var fullText = new StringBuilder();
        await foreach (var token in _aiService.StreamAsync(streamMessages, streamOptions, ct))
        {
            fullText.Append(token);
            yield return new RagStreamChunk { Type = "token", Value = token };
        }

        var assistantId = await PersistAsync(sessionId, request.Message, fullText.ToString(), refused: false, ct,
            request.AttachmentName, request.AttachmentUrl, request.AttachmentContentType);
        yield return new RagStreamChunk { Type = "done", SessionId = sessionId, WasRefused = false, AssistantMessageId = assistantId };
    }

    // ── Message Builder ───────────────────────────────────────────────────────
    // Returns [SystemMessage(context), UserMessage(question)] so tool-call/result
    // turns inserted by UseFunctionInvocation() middleware fit cleanly in between.

    private static IList<ChatMessage> BuildMessages(
        string question,
        string? attachmentName,
        IReadOnlyList<(string ParentText, string FileName)> chunks,
        IReadOnlyList<ChatSessionHistory> history,
        IReadOnlyList<ChatSessionHistory> goldenPairs)
    {
        var system = new StringBuilder();

        // Role boundary — must come first so it takes priority over everything below
        system.AppendLine("You are an Oil & Gas specialist AI assistant.");
        system.AppendLine("You ONLY answer questions related to the oil and gas industry, including: drilling, production, reservoir engineering, pipelines, refining, HSE, petrochemicals, and energy markets.");
        system.AppendLine("If the user sends a greeting (such as 'hi', 'hello', 'good morning', 'hey', etc.), respond warmly and invite them to ask an Oil & Gas related question. For example: \"Hello! Welcome to the Oil & Gas AI Assistant. How can I help you today? Feel free to ask me anything about drilling, production, reservoir engineering, pipelines, HSE, or refining!\"");
        system.AppendLine("If the user asks about anything outside Oil & Gas topics — such as finance, gold, stocks, general science, coding, or any other unrelated subject — respond ONLY with:");
        system.AppendLine("\"My apologies, but I'm only able to assist with Oil & Gas related topics. I'd be happy to help you with questions about drilling, production, reservoir engineering, pipelines, HSE, or refining. Please feel free to ask anything within those areas!\"");
        system.AppendLine("Do not attempt to answer off-topic questions, even partially.");
        system.AppendLine();

        // 0. Few-shot golden examples
        if (goldenPairs.Count > 0)
        {
            system.AppendLine("[VERIFIED EXAMPLES — expert-confirmed answers]");
            foreach (var pair in goldenPairs)
                system.AppendLine($"EXAMPLE ANSWER: {pair.Message}");
            system.AppendLine("[END EXAMPLES]");
            system.AppendLine();
        }

        // 1. Document context
        if (chunks.Count > 0)
        {
            system.AppendLine("[CONTEXT START]");
            foreach (var (text, file) in chunks)
            {
                system.AppendLine($"Source: {file}");
                system.AppendLine(text);
                system.AppendLine("---");
            }
            system.AppendLine("[CONTEXT END]");
            system.AppendLine("Use the context above to answer the Oil & Gas question accurately. If the context does not contain the answer, say so clearly. Never fabricate.");
        }
        else
        {
            system.AppendLine("No document context available. Answer using your Oil & Gas domain knowledge. Never fabricate.");
        }

        // 2. Conversation history
        if (history.Count > 0)
        {
            system.AppendLine("\nCONVERSATION HISTORY:");
            foreach (var h in history)
                system.AppendLine($"{h.Role.ToUpper()}: {h.Message}");
        }

        // 3. User message carries only the question (+ attachment note if any)
        var userText = attachmentName is not null
            ? $"[User attached a file: {attachmentName}]\n{question}"
            : question;

        return
        [
            new ChatMessage(ChatRole.System, system.ToString()),
            new ChatMessage(ChatRole.User, userText)
        ];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Returns the DB row ID of the assistant message so the client can reference it for feedback.
    private async Task<long> PersistAsync(
        Guid sessionId,
        string question,
        string answer,
        bool refused,
        CancellationToken ct,
        string? attachmentName = null,
        string? attachmentUrl = null,
        string? attachmentContentType = null)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var userMsg = new ChatSessionHistory
            {
                SessionId = sessionId,
                Role = "user",
                Message = question,
                AttachmentName = attachmentName,
                AttachmentUrl = attachmentUrl,
                AttachmentContentType = attachmentContentType
            };
            var assistantMsg = new ChatSessionHistory
            {
                SessionId = sessionId,
                Role = "assistant",
                Message = answer,
                WasRefused = refused
            };
            db.ChatHistory.AddRange(userMsg, assistantMsg);
            await db.SaveChangesAsync(ct);
            return assistantMsg.Id;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to persist chat history for session {SessionId}", sessionId);
            return 0;
        }
    }

    private static string Hash(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}