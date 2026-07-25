using OilGasAI.API.Models;

namespace OilGasAI.API.Interfaces;

public interface IDocumentIngestionService
{
    /// <summary>Saves document metadata and queues PDF for background processing. Returns immediately (202).</summary>
    Task<Document> QueueAsync(Stream pdfStream, string fileName, string contentType, long size, CancellationToken ct = default);

    /// <summary>
    /// Called after the user reviews a PendingReview document and decides what to do.
    /// replace   → supersedes the specified old documents, then indexes the new one.
    /// keep-both → indexes the new document alongside the existing ones.
    /// cancel    → deletes the pending document and its chunks entirely.
    /// </summary>
    Task<Document> ResolveConflictAsync(Guid documentId, string action, IReadOnlyList<Guid>? replaceIds, CancellationToken ct = default);
}