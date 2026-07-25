using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Interfaces;
using OilGasAI.API.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OilGasAI.API.Services;

/// <summary>
/// Thread-safe bounded queue between the HTTP upload endpoint and the background worker.
/// Uses COMPOSITION (holds Channel internally) — NOT inheritance because Channel&lt;T&gt;
/// is abstract and causes CS0144 if extended directly.
/// </summary>
public sealed class DocumentIngestionQueue
{
    private readonly Channel<(Guid DocumentId, MemoryStream Stream)> _channel;
    public ChannelReader<(Guid, MemoryStream)> Reader => _channel.Reader;
    public ChannelWriter<(Guid, MemoryStream)> Writer => _channel.Writer;

    public DocumentIngestionQueue()
    {
        _channel = Channel.CreateBounded<(Guid, MemoryStream)>(
            new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,  // only IngestionBackgroundService reads
                SingleWriter = false  // multiple HTTP requests can write
            });
    }
}

// ── Service ───────────────────────────────────────────────────────────────────

public class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAIService _aiService;        // ✅ unified — replaces IEmbeddingService
    private readonly IQdrantService _qdrant;
    private readonly DocumentIngestionQueue _queue;
    private readonly ILogger<DocumentIngestionService> _log;
    private readonly float _conflictThreshold;

    private const long MaxFileSizeBytes = 50 * 1024 * 1024;
    private const int EmbeddingBatchSize = 10;

    public DocumentIngestionService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAIService aiService,
        IQdrantService qdrant,
        DocumentIngestionQueue queue,
        IConfiguration config,
        ILogger<DocumentIngestionService> log)
    {
        _dbFactory = dbFactory;
        _aiService = aiService;
        _qdrant = qdrant;
        _queue = queue;
        _log = log;
        // Minimum centroid similarity required to flag a conflict. Below this the documents
        // are treated as unrelated regardless of content overlap.
        _conflictThreshold = config.GetValue<float>("Documents:ConflictSimilarityThreshold", 0.55f);
    }

    // ── QueueAsync ────────────────────────────────────────────────────────────

    public async Task<Document> QueueAsync(
        Stream pdfStream,
        string fileName,
        string contentType,
        long size,
        CancellationToken ct = default)
    {
        // Guard: reject files that are too large
        if (size > MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"File '{fileName}' exceeds the maximum allowed size of 50 MB.");

        // Buffer PDF into memory so the HTTP request stream can close immediately
        var ms = new MemoryStream((int)size);
        await pdfStream.CopyToAsync(ms, ct);
        ms.Position = 0;

        // Persist document metadata
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var doc = new Document
        {
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = size,
            Status = DocumentStatus.Pending
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);

        // Enqueue for background processing — returns immediately (202)
        await _queue.Writer.WriteAsync((doc.Id, ms), ct);
        _log.LogInformation(
            "Document {Id} '{Name}' queued for ingestion.", doc.Id, fileName);

        return doc;
    }

    // ── ProcessAsync (called by background worker) ────────────────────────────

    public async Task ProcessAsync(Guid documentId, MemoryStream stream, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var doc = await db.Documents.FindAsync([documentId], ct)
            ?? throw new InvalidOperationException($"Document {documentId} not found.");

        try
        {
            // Mark as processing immediately so we can detect mid-crash state
            doc.Status = DocumentStatus.Processing;
            await db.SaveChangesAsync(ct);

            // Step 1: Extract text from PDF
            stream.Position = 0;
            var pages = new List<string>();
            using (var pdf = PdfDocument.Open(stream))
                foreach (var page in pdf.GetPages())
                {
                    var text = ContentOrderTextExtractor.GetText(page);
                    if (!string.IsNullOrWhiteSpace(text))
                        pages.Add(text);
                }

            if (pages.Count == 0)
            {
                doc.Status = DocumentStatus.Failed;
                doc.ErrorMessage = "No extractable text. PDF may be scanned — OCR not yet supported.";
                await db.SaveChangesAsync(ct);
                return;
            }

            var fullText = string.Join("\n\n", pages);
            _log.LogInformation(
                "Document {Id}: extracted {Chars} characters from {Pages} pages.",
                documentId, fullText.Length, pages.Count);

            // Step 2: Create parent-child chunk pairs
            var pairs = TextChunker.CreatePairs(fullText);
            _log.LogInformation(
                "Document {Id}: {N} chunk pairs created.", documentId, pairs.Count);

            // Step 3: Embed child chunks in batches
            // "search_document:" prefix is REQUIRED for nomic-embed-text retrieval quality
            var allChunks = new List<(Guid Id, float[] Vector, string ChildText, Guid DocId, int Idx)>();

            for (int i = 0; i < pairs.Count; i += EmbeddingBatchSize)
            {
                var slice = pairs.Skip(i).Take(EmbeddingBatchSize).ToList();
                var inputs = slice.Select(p => $"search_document: {p.ChildText}").ToList();

                var vecs = await Task.WhenAll(
                    inputs.Select(input => _aiService.GetEmbeddingAsync(input, ct)));

                for (int j = 0; j < slice.Count; j++)
                    allChunks.Add((Guid.NewGuid(), vecs[j], slice[j].ChildText, documentId, slice[j].ChunkIndex));

                _log.LogInformation(
                    "Document {Id}: embedded batch {Batch}/{Total}.",
                    documentId, i / EmbeddingBatchSize + 1,
                    (int)Math.Ceiling(pairs.Count / (double)EmbeddingBatchSize));
            }

            // Step 4: Save parent chunks to PostgreSQL (rich LLM context)
            db.DocumentChunks.AddRange(allChunks.Zip(pairs, (c, p) => new DocumentChunk
            {
                Id = c.Id,
                DocumentId = documentId,
                ChildText = p.ChildText,
                ParentText = p.ParentText,
                ChunkIndex = p.ChunkIndex
            }));
            doc.ChunkCount = pairs.Count;

            // Step 5: Compute full-document fingerprint (centroid of all chunk embeddings).
            // Using ALL chunk vectors gives a semantic fingerprint that represents every page,
            // every section — not just the opening paragraph.
            var centroid = ComputeCentroid(allChunks.Select(c => c.Vector).ToList());
            doc.FingerprintJson = JsonSerializer.Serialize(centroid);

            // Step 6: Compare fingerprint against every already-indexed document.
            // Always surface conflicts to the user — they decide what to do.
            // The similarity % is shown so they can judge relevance themselves.
            var existingDocs = await db.Documents
                .Where(d => d.Id != documentId
                         && d.Status == DocumentStatus.Indexed
                         && d.FingerprintJson != null)
                .Select(d => new { d.Id, d.FileName, d.FingerprintJson })
                .AsNoTracking()
                .ToListAsync(ct);

            var conflicts = new List<DocumentConflict>();
            foreach (var existing in existingDocs)
            {
                var existingFp = JsonSerializer.Deserialize<float[]>(existing.FingerprintJson!);
                if (existingFp is null) continue;
                var sim = CosineSimilarity(centroid, existingFp);
                if (sim >= _conflictThreshold)
                    conflicts.Add(new DocumentConflict
                    {
                        DocumentId = existing.Id,
                        FileName   = existing.FileName,
                        Similarity = sim
                    });
            }

            if (conflicts.Count > 0)
            {
                // Conflict found — hold the chunks in PostgreSQL but do NOT push to Qdrant yet.
                // The user must review and decide before this document affects search results.
                conflicts.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));
                doc.ConflictsJson = JsonSerializer.Serialize(conflicts);
                doc.Status = DocumentStatus.PendingReview;

                _log.LogInformation(
                    "Document {Id} '{Name}' is pending user review — {N} similar document(s) found (threshold {T:F2}).",
                    documentId, doc.FileName, conflicts.Count, _conflictThreshold);
            }
            else
            {
                // Step 7: No conflicts — upsert to Qdrant and mark indexed immediately.
                await _qdrant.UpsertChunksAsync(allChunks, ct);
                doc.Status    = DocumentStatus.Indexed;
                doc.IndexedAt = DateTimeOffset.UtcNow;

                _log.LogInformation(
                    "Document {Id} '{Name}' indexed successfully with {N} chunks.",
                    documentId, doc.FileName, pairs.Count);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ingestion failed for document {Id}.", documentId);
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ex.Message[..Math.Min(1000, ex.Message.Length)];
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Conflict resolution ───────────────────────────────────────────────────

    public async Task<Document> ResolveConflictAsync(
        Guid documentId,
        string action,
        IReadOnlyList<Guid>? replaceIds,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var doc = await db.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new InvalidOperationException($"Document {documentId} not found.");

        if (doc.Status != DocumentStatus.PendingReview)
            throw new InvalidOperationException("Document is not awaiting review.");

        if (action == "cancel")
        {
            db.Documents.Remove(doc); // cascades to DocumentChunks
            await db.SaveChangesAsync(ct);
            _log.LogInformation("Document {Id} '{Name}' upload cancelled by user.", documentId, doc.FileName);
            return doc;
        }

        if (action == "replace" && replaceIds?.Count > 0)
        {
            var oldDocs = await db.Documents
                .Where(d => replaceIds.Contains(d.Id))
                .ToListAsync(ct);

            foreach (var old in oldDocs)
            {
                await _qdrant.DeleteDocumentAsync(old.Id, ct);
                old.Status       = DocumentStatus.Superseded;
                old.SupersededAt = DateTimeOffset.UtcNow;
                _log.LogInformation(
                    "Document {Id} '{Name}' superseded by {NewId}.", old.Id, old.FileName, documentId);
            }
        }

        // Re-embed child chunks and push to Qdrant.
        // Vectors were computed during ProcessAsync but not stored — re-compute from ChildText.
        var chunksToIndex = doc.Chunks.OrderBy(c => c.ChunkIndex).ToList();
        var qdrantChunks  = new List<(Guid Id, float[] Vector, string ChildText, Guid DocId, int Idx)>();

        for (int i = 0; i < chunksToIndex.Count; i += EmbeddingBatchSize)
        {
            var slice  = chunksToIndex.Skip(i).Take(EmbeddingBatchSize).ToList();
            var inputs = slice.Select(c => $"search_document: {c.ChildText}").ToList();
            var vecs   = await Task.WhenAll(inputs.Select(t => _aiService.GetEmbeddingAsync(t, ct)));

            for (int j = 0; j < slice.Count; j++)
                qdrantChunks.Add((slice[j].Id, vecs[j], slice[j].ChildText, documentId, slice[j].ChunkIndex));
        }

        await _qdrant.UpsertChunksAsync(qdrantChunks, ct);

        doc.Status        = DocumentStatus.Indexed;
        doc.IndexedAt     = DateTimeOffset.UtcNow;
        doc.ConflictsJson = null;

        await db.SaveChangesAsync(ct);
        _log.LogInformation(
            "Document {Id} '{Name}' conflict resolved (action={Action}), now indexed.",
            documentId, doc.FileName, action);

        return doc;
    }

    // ── Math helpers ─────────────────────────────────────────────────────────

    private static float[] ComputeCentroid(IReadOnlyList<float[]> vectors)
    {
        var dim = vectors[0].Length;
        var avg = new float[dim];
        foreach (var v in vectors)
            for (int i = 0; i < dim; i++) avg[i] += v[i] / vectors.Count;
        return avg;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na < 1e-10 || nb < 1e-10 ? 0f : (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
    }
}

// ── Background Worker ─────────────────────────────────────────────────────────

/// <summary>
/// Reads from DocumentIngestionQueue and calls DocumentIngestionService.ProcessAsync.
/// Upload endpoint returns 202 immediately — processing happens here asynchronously.
/// </summary>
public sealed class IngestionBackgroundService : BackgroundService
{
    private readonly DocumentIngestionQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IngestionBackgroundService> _log;

    public IngestionBackgroundService(
        DocumentIngestionQueue queue,
        IServiceScopeFactory scopes,
        ILogger<IngestionBackgroundService> log)
    {
        _queue = queue;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Ingestion background service started.");

        await foreach (var (docId, stream) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = _scopes.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<DocumentIngestionService>();

            try
            {
                await svc.ProcessAsync(docId, stream, stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error processing document {Id}.", docId);
            }
            finally
            {
                await stream.DisposeAsync(); // always release memory
            }
        }
    }
}