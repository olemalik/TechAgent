using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Models;
using OilGasAI.API.Interfaces;

namespace OilGasAI.API.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestion;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DocumentController(IDocumentIngestionService ingestion, IDbContextFactory<AppDbContext> dbFactory)
    {
        _ingestion = ingestion;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// POST /api/documents/upload
    /// Upload a PDF. Returns 202 Accepted immediately — processing happens in background.
    /// Poll GET /api/documents/{id}/status to see when indexing is complete.
    ///
    /// The PDF is split into chunks, embedded via nomic-embed-text, and stored in Qdrant.
    /// After indexing, the AI will automatically use the document when answering related questions.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]  // 50 MB max
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (!file.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only PDF files are supported." });

        if (file.Length > 50_000_000)
            return BadRequest(new { error = "File exceeds 50 MB limit." });

        await using var stream = file.OpenReadStream();
        var doc = await _ingestion.QueueAsync(stream, file.FileName, file.ContentType, file.Length, ct);

        return Accepted(new DocumentUploadResponse
        {
            Id = doc.Id,
            FileName = doc.FileName,
            Status = doc.Status,
            Message = "Queued for processing. Poll /status to check progress."
        });
    }

    /// <summary>
    /// GET /api/documents/{id}/status
    /// Check if a document has been indexed.
    /// Status values: Processing | Indexed | Failed
    /// </summary>
    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var raw = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id)
            .FirstOrDefaultAsync(ct);

        if (raw is null) return NotFound();

        var doc = new DocumentStatusResponse
        {
            Id           = raw.Id,
            FileName     = raw.FileName,
            Status       = raw.Status,
            ChunkCount   = raw.ChunkCount,
            ErrorMessage = raw.ErrorMessage,
            IndexedAt    = raw.IndexedAt,
            SeriesId     = raw.SeriesId,
            Conflicts    = raw.Status == DocumentStatus.PendingReview && raw.ConflictsJson != null
                               ? JsonSerializer.Deserialize<List<DocumentConflict>>(raw.ConflictsJson)
                               : null
        };

        return Ok(doc);
    }

    /// <summary>
    /// GET /api/documents
    /// List all documents except Superseded (those are replaced versions, kept for audit in the DB).
    /// PendingReview documents include their conflict list so the UI can show the resolution dialog.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var raw = await db.Documents
            .AsNoTracking()
            .Where(d => d.Status != DocumentStatus.Superseded)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(ct);

        var docs = raw.Select(d => new DocumentStatusResponse
        {
            Id           = d.Id,
            FileName     = d.FileName,
            Status       = d.Status,
            ChunkCount   = d.ChunkCount,
            IndexedAt    = d.IndexedAt,
            ErrorMessage = d.ErrorMessage,
            SeriesId     = d.SeriesId,
            Conflicts    = d.Status == DocumentStatus.PendingReview && d.ConflictsJson != null
                               ? JsonSerializer.Deserialize<List<DocumentConflict>>(d.ConflictsJson)
                               : null
        }).ToList();

        return Ok(docs);
    }

    /// <summary>
    /// POST /api/documents/{id}/resolve
    /// Resolve a PendingReview document. The user must explicitly choose an action:
    ///   replace   — supersede the specified old documents, then index the new one.
    ///   keep-both — index the new document alongside the existing ones.
    ///   cancel    — discard the new document entirely.
    /// </summary>
    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveConflictRequest req, CancellationToken ct)
    {
        if (req.Action is not ("replace" or "keep-both" or "add-to-series" or "cancel"))
            return BadRequest("Action must be 'replace', 'keep-both', 'add-to-series', or 'cancel'.");

        try
        {
            var doc = await _ingestion.ResolveConflictAsync(id, req.Action, req.ReplaceIds, ct);
            return Ok(new { documentId = doc.Id, status = doc.Status, action = req.Action });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}