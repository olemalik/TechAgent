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
    public DateTimeOffset? SupersededAt { get; set; }

    // Centroid of all child-chunk embeddings — represents the full document semantically.
    // Used to detect similar documents on re-upload without relying on filename.
    public string? FingerprintJson { get; set; }

    // Serialized List<DocumentConflict> — populated when Status = PendingReview.
    public string? ConflictsJson { get; set; }

    // Non-null when this document belongs to a recurring series (e.g. daily inspection checklists).
    // Documents in the same series are auto-indexed on re-upload without the conflict dialog.
    public Guid? SeriesId { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}

public static class DocumentStatus
{
    public const string Pending       = "Pending";
    public const string Processing    = "Processing";
    public const string Indexed       = "Indexed";
    public const string Failed        = "Failed";
    public const string PendingReview = "PendingReview";
    public const string Superseded    = "Superseded";
}
