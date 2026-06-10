namespace OilGasAI.API.Services;

public interface IQdrantService
{
    /// <summary>
    /// Creates the Qdrant collection if it does not already exist.
    /// </summary>

    Task EnsureCollectionAsync(CancellationToken ct = default);
    /// <summary>
    /// Upserts a batch of document chunks into Qdrant.
    /// </summary>

    Task UpsertChunksAsync(IReadOnlyList<(Guid Id, float[] Vector, string ChildText, Guid DocumentId, int Index)> chunks, CancellationToken ct = default);

    /// <summary>
    /// Searches Qdrant for the top-K most similar chunks to the query vector.
    /// </summary>
    Task<IReadOnlyList<(Guid ChunkId, float Score)>> SearchAsync(float[] queryVector, int limit = 4, CancellationToken ct = default);

    /// <summary>
    /// Deletes all chunks belonging to a specific document.
    /// </summary>
    Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);
}
