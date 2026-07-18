using Microsoft.AspNetCore.Mvc;
using OilGasAI.API.Models;

namespace OilGasAI.API.Controllers;

/// <summary>
/// Handles general-purpose file uploads from the chat input.
/// Files are stored under wwwroot/uploads/ and served as static files.
/// This is separate from /api/documents/upload which indexes PDFs into the RAG vector store.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FileController : ControllerBase
{
    private const long MaxFileBytes = 20 * 1024 * 1024; // 20 MB

    private static readonly HashSet<string> AllowedTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain",
        "text/csv",
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
    ];

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FileController> _log;

    public FileController(IWebHostEnvironment env, ILogger<FileController> log)
    {
        _env = env;
        _log = log;
    }

    /// <summary>
    /// POST /api/file/upload
    /// Accepts a single file from the chat input.
    /// Returns a URL the client includes in the next chat message.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        if (file.Length > MaxFileBytes)
            return BadRequest("File exceeds the 20 MB limit.");

        if (!AllowedTypes.Contains(file.ContentType))
            return BadRequest($"File type '{file.ContentType}' is not allowed.");

        var webRoot = _env.WebRootPath ?? System.IO.Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadsPath = System.IO.Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(uploadsPath);

        // Use a GUID prefix to prevent collisions; keep the original extension
        var ext = System.IO.Path.GetExtension(file.FileName);
        var storedName = $"{Guid.NewGuid()}{ext}";
        var filePath = System.IO.Path.Combine(uploadsPath, storedName);

        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream, ct);

        _log.LogInformation("Chat file uploaded: {FileName} → {StoredName}", file.FileName, storedName);

        return Ok(new FileUploadResponse
        {
            FileName = file.FileName,
            Url = $"/uploads/{storedName}",
            ContentType = file.ContentType,
            SizeBytes = file.Length
        });
    }
}
