// Models/Document.cs
namespace OilGasAI.API.Models;

public class Document
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int ChunkCount { get; set; }
    public string Status { get; set; } = DocumentStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? IndexedAt { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}

public static class DocumentStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Indexed = "Indexed";
    public const string Failed = "Failed";
}
