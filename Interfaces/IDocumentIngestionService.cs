using OilGasAI.API.Models;

namespace OilGasAI.API.Services;

public interface IDocumentIngestionService
{
    /// <summary>Saves document metadata and queues PDF for background processing. Returns immediately (202).</summary>
    Task<Document> QueueAsync(Stream pdfStream, string fileName, string contentType, long size, CancellationToken ct = default);
}