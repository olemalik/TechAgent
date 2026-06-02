using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Models;
using System.Threading.Channels;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OilGasAI.API.Services;
// ─── Background queue (COMPOSITION pattern — Channel<T> is abstract, cannot be extended) ──────

/// <summary>
/// Thread-safe bounded queue between the HTTP upload endpoint and the background worker.
/// Uses COMPOSITION (holds Channel internally, exposes Reader/Writer) — NOT inheritance,
/// because Channel&lt;T&gt; is abstract and causes CS0144 if you try to extend it directly.
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
                SingleReader = true,
                SingleWriter = false
            });
    }
}

// ─── Service ──────────────────────────────────────────────────────────────────

public class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IEmbeddingService _embedding;
    private readonly IQdrantService _qdrant;
    private readonly IDomainGuardService _guard;
    private readonly DocumentIngestionQueue _queue;
    private readonly ILogger<DocumentIngestionService> _log;

    public DocumentIngestionService(
        IDbContextFactory<AppDbContext> dbFactory,
        IEmbeddingService embedding,
        IQdrantService qdrant,
        IDomainGuardService guard,
        DocumentIngestionQueue queue,
        ILogger<DocumentIngestionService> log)
    {
        _dbFactory = dbFactory; _embedding = embedding;
        _qdrant = qdrant; _guard = guard; _queue = queue; _log = log;
    }

    public async Task<Document> QueueAsync(Stream pdfStream, string fileName, string contentType, long size, CancellationToken ct = default)
    {
        var ms = new MemoryStream((int)Math.Min(size, int.MaxValue));
        await pdfStream.CopyToAsync(ms, ct);
        ms.Position = 0;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var doc = new Document { FileName = fileName, ContentType = contentType, SizeBytes = size };
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);

        await _queue.Writer.WriteAsync((doc.Id, ms), ct);
        _log.LogInformation("Document {Id} '{Name}' queued for ingestion.", doc.Id, fileName);
        return doc;
    }

    /// <summary>Called by IngestionBackgroundService — runs outside the HTTP request scope.</summary>
    public async Task ProcessAsync(Guid documentId, MemoryStream stream, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var doc = await db.Documents.FindAsync([documentId], ct)
                  ?? throw new InvalidOperationException($"Document {documentId} not found.");

        try
        {
            // 1. Extract text — PdfPig works on text-layer PDFs only (not scanned/image PDFs)
            stream.Position = 0;
            var pages = new List<string>();
            using (var pdf = PdfDocument.Open(stream))
                foreach (var page in pdf.GetPages())
                {
                    var text = ContentOrderTextExtractor.GetText(page);
                    if (!string.IsNullOrWhiteSpace(text)) pages.Add(text);
                }

            if (pages.Count == 0)
            {
                doc.Status = "Failed";
                doc.ErrorMessage = "No extractable text. PDF may be scanned — OCR support not yet enabled.";
                await db.SaveChangesAsync(ct);
                return;
            }

            var fullText = string.Join("\n\n", pages);

            // Soft O&G domain check on document content
            if (!await _guard.IsAllowedAsync(fullText[..Math.Min(600, fullText.Length)], null, ct))
                _log.LogWarning("Document '{Name}' has weak Oil & Gas signal.", doc.FileName);

            // 2. Create parent-child chunk pairs
            var pairs = TextChunker.CreatePairs(fullText);
            _log.LogInformation("Document {Id}: {N} chunk pairs.", documentId, pairs.Count);

            // 3. Embed child chunks in batches of 10
            var allChunks = new List<(Guid Id, float[] Vector, string ChildText, Guid DocId, int Idx)>();
            const int batch = 10;

            for (int i = 0; i < pairs.Count; i += batch)
            {
                var slice = pairs.Skip(i).Take(batch).ToList();
                // "search_document:" prefix is REQUIRED for correct nomic-embed-text retrieval quality
                var inputs = slice.Select(p => $"search_document: {p.ChildText}").ToList();
                var vecs = await _embedding.EmbedBatchAsync(inputs, ct);
                for (int j = 0; j < slice.Count; j++)
                    allChunks.Add((Guid.NewGuid(), vecs[j], slice[j].ChildText, documentId, slice[j].ChunkIndex));
            }

            // 4. Save parent chunks to PostgreSQL (for rich LLM context)
            db.DocumentChunks.AddRange(allChunks.Zip(pairs, (c, p) => new DocumentChunk
            {
                Id = c.Id,
                DocumentId = documentId,
                ChildText = p.ChildText,
                ParentText = p.ParentText,
                ChunkIndex = p.ChunkIndex
            }));

            // 5. Upsert child embeddings into Qdrant
            await _qdrant.UpsertChunksAsync(allChunks, ct);

            doc.ChunkCount = pairs.Count;
            doc.Status = "Indexed";
            doc.IndexedAt = DateTimeOffset.UtcNow;
            _log.LogInformation("Document {Id} indexed: {N} chunks.", documentId, pairs.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ingestion failed for document {Id}.", documentId);
            doc.Status = "Failed";
            doc.ErrorMessage = ex.Message[..Math.Min(1000, ex.Message.Length)];
        }

        await db.SaveChangesAsync(ct);
    }
}

// ─── Background Worker ────────────────────────────────────────────────────────

/// <summary>
/// Reads from DocumentIngestionQueue and calls DocumentIngestionService.ProcessAsync.
/// Runs as a hosted service — upload endpoint returns 202 immediately, processing happens here.
/// </summary>
public sealed class IngestionBackgroundService : BackgroundService
{
    private readonly DocumentIngestionQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IngestionBackgroundService> _log;

    public IngestionBackgroundService(DocumentIngestionQueue queue, IServiceScopeFactory scopes, ILogger<IngestionBackgroundService> log)
    {
        _queue = queue; _scopes = scopes; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Ingestion background service started.");
        await foreach (var (docId, stream) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = _scopes.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<DocumentIngestionService>();
            try { await svc.ProcessAsync(docId, stream, stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Unhandled error for document {Id}.", docId); }
            finally { await stream.DisposeAsync(); }
        }
    }
}