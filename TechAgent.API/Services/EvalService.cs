using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Interfaces;
using OilGasAI.API.Models;
using System.Text;

namespace OilGasAI.API.Services;

/// <summary>
/// Runs the golden evaluation set against the live RAG pipeline and reports two metrics:
///
///   Recall@K    — Was the expected source document retrieved in the top-K Qdrant results?
///                 If this is below 0.80, fix the retrieval layer first. Fine-tuning cannot
///                 compensate for a retrieval problem.
///
///   Faithfulness — What fraction of the expected key phrases appear in the actual answer?
///                 Only investigate this once Recall@K is healthy. A low score here means
///                 the generation layer is the bottleneck (consider RAFT / system-prompt tuning).
///
/// NOTE: The domain guard is intentionally bypassed here. Golden-set questions are by definition
///       O&G questions; running them through the guard would conflate guard accuracy with RAG accuracy.
///       The cache is also bypassed so every run reflects the current model state.
/// </summary>
public class EvalService
{
    private readonly IAIService _aiService;
    private readonly IQdrantService _qdrant;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EvalService> _log;

    private const int TopK = 4;

    public EvalService(
        IAIService aiService,
        IQdrantService qdrant,
        IDbContextFactory<AppDbContext> dbFactory,
        IConfiguration config,
        ILogger<EvalService> log)
    {
        _aiService = aiService;
        _qdrant = qdrant;
        _dbFactory = dbFactory;
        _config = config;
        _log = log;
    }

    public async Task<EvalReport> RunAsync(IReadOnlyList<GoldenEntry> entries, CancellationToken ct = default)
    {
        var minScore = _config.GetValue<float>("Qdrant:MinSimilarityScore", 0.45f);
        var domainThreshold = _config.GetValue<float>("DomainGuard:SimilarityThreshold", 0.30f);

        var results = new List<EvalEntryResult>(entries.Count);
        int errorCount = 0;

        foreach (var entry in entries)
        {
            _log.LogInformation("Eval [{Id}]: {Q}", entry.Id, entry.Question[..Math.Min(80, entry.Question.Length)]);
            var result = await EvalOneAsync(entry, minScore, ct);
            if (result.ErrorMessage is not null) errorCount++;
            results.Add(result);
        }

        int recallHits = results.Count(r => r.RecallHit);
        double avgFaith = results.Count > 0
            ? results.Average(r => r.Faithfulness)
            : 0;

        return new EvalReport
        {
            TotalQuestions = entries.Count,
            Errors = errorCount,
            RecallAtK = entries.Count > 0 ? (double)recallHits / entries.Count : 0,
            AvgFaithfulness = avgFaith,
            TopK = TopK,
            MinSimilarityScore = minScore,
            DomainGuardThreshold = domainThreshold,
            Results = results
        };
    }

    private async Task<EvalEntryResult> EvalOneAsync(GoldenEntry entry, float minScore, CancellationToken ct)
    {
        var result = new EvalEntryResult
        {
            Id = entry.Id,
            Question = entry.Question,
            SourceDocument = entry.SourceDocument,
            KeyPhrasesTotal = entry.KeyPhrases.Count
        };

        try
        {
            // Step 1: Embed question
            var queryVec = await _aiService.GetEmbeddingAsync(entry.Question, ct);

            // Step 2: Qdrant search — same TopK and threshold as production
            var hits = await _qdrant.SearchAsync(queryVec, TopK, ct);
            var relevantHits = hits.Where(h => h.Score >= minScore).ToList();
            var chunkIds = relevantHits.Select(h => h.ChunkId).ToList();

            // Step 3: Load chunk file names from PostgreSQL
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
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

            // Build retrieved doc list for the report (sorted by Qdrant score, highest first)
            result.Retrieved = relevantHits
                .Where(h => chunkLookup.ContainsKey(h.ChunkId))
                .Select(h => new RetrievedDoc { FileName = chunkLookup[h.ChunkId].FileName, Score = h.Score })
                .ToList();

            // Step 4: Recall@K — is the expected source doc in the retrieved set?
            var retrievedFileNames = result.Retrieved.Select(r => r.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.RecallHit = retrievedFileNames.Contains(entry.SourceDocument);

            // Step 5: Generate answer via the same RAG prompt (no cache, no domain guard)
            var prompt = BuildEvalPrompt(entry.Question, chunks.Select(c => (c.ParentText, c.FileName)).ToList());
            result.ActualAnswer = await _aiService.ChatAsync(prompt, ct);

            // Step 6: Faithfulness — count key phrases found in the answer
            var lowerAnswer = result.ActualAnswer.ToLowerInvariant();
            result.KeyPhrasesHit = entry.KeyPhrases.Count(kp => lowerAnswer.Contains(kp.ToLowerInvariant()));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Eval [{Id}] failed", entry.Id);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static string BuildEvalPrompt(
        string question,
        IReadOnlyList<(string ParentText, string FileName)> chunks)
    {
        var sb = new StringBuilder();
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
}