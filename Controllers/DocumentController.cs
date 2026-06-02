using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Models;
using OilGasAI.API.Services;

namespace OilGasAI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
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
        var doc = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DocumentStatusResponse
            {
                Id = d.Id,
                FileName = d.FileName,
                Status = d.Status,
                ChunkCount = d.ChunkCount,
                ErrorMessage = d.ErrorMessage,
                IndexedAt = d.IndexedAt
            })
            .FirstOrDefaultAsync(ct);

        return doc is null ? NotFound() : Ok(doc);
    }

    /// <summary>
    /// GET /api/documents
    /// List all uploaded documents with their status.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var docs = await db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentStatusResponse
            {
                Id = d.Id,
                FileName = d.FileName,
                Status = d.Status,
                ChunkCount = d.ChunkCount,
                IndexedAt = d.IndexedAt
            })
            .ToListAsync(ct);
        return Ok(docs);
    }
}