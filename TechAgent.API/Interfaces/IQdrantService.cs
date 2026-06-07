namespace OilGasAI.API.Services;

public interface IQdrantService
{
    Task EnsureCollectionAsync(CancellationToken ct = default);
    Task UpsertChunksAsync(IReadOnlyList<(Guid Id, float[] Vector, string ChildText, Guid DocumentId, int Index)> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<(Guid ChunkId, float Score)>> SearchAsync(float[] queryVector, int limit = 4, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);
}
