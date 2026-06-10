// Services/QdrantService.cs
using Qdrant.Client;
using Qdrant.Client.Grpc;
using OilGasAI.API.Interfaces;

namespace OilGasAI.API.Services;

public class QdrantService : IQdrantService
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantService> _log;
    private readonly string _collection;
    private readonly int _vectorSize;

    public QdrantService(IConfiguration config, ILogger<QdrantService> log)
    {
        _log = log;
        _collection = config["Qdrant:CollectionName"] ?? "oilgas-chunks";
        _vectorSize = int.Parse(config["Qdrant:VectorSize"] ?? "768");

        var host = config["Qdrant:Host"] ?? "localhost";
        var port = int.Parse(config["Qdrant:Port"] ?? "6334");

        _client = new QdrantClient(host, port);
    }

    // ── 1. EnsureCollectionAsync ──────────────────────────────────────────────

    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(ct);
            var exists = collections.Any(c => c == _collection);

            if (exists)
            {
                _log.LogInformation("Qdrant collection '{Collection}' already exists.", _collection);
                return;
            }

            await _client.CreateCollectionAsync(
                _collection,
                new VectorParams
                {
                    Size = (ulong)_vectorSize,
                    Distance = Distance.Cosine
                },
                cancellationToken: ct);

            _log.LogInformation(
                "Qdrant collection '{Collection}' created with vector size {Size}.",
                _collection, _vectorSize);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to ensure Qdrant collection '{Collection}'.", _collection);
            throw;
        }
    }

    // ── 2. UpsertChunksAsync ──────────────────────────────────────────────────

    public async Task UpsertChunksAsync(
        IReadOnlyList<(Guid Id, float[] Vector, string ChildText, Guid DocumentId, int Index)> chunks,
        CancellationToken ct = default)
    {
        if (chunks.Count == 0) return;

        try
        {
            var points = chunks.Select(c => new PointStruct
            {
                Id = new PointId { Uuid = c.Id.ToString() },
                Vectors = c.Vector,
                Payload =
                {
                    ["child_text"]    = c.ChildText,
                    ["document_id"]   = c.DocumentId.ToString(),
                    ["chunk_index"]   = c.Index
                }
            }).ToList();

            await _client.UpsertAsync(_collection, points, cancellationToken: ct);

            _log.LogInformation(
                "Upserted {Count} chunks into Qdrant collection '{Collection}'.",
                chunks.Count, _collection);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to upsert chunks into Qdrant.");
            throw;
        }
    }

    // ── 3. SearchAsync ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<(Guid ChunkId, float Score)>> SearchAsync(
        float[] queryVector,
        int limit = 4,
        CancellationToken ct = default)
    {
        try
        {
            var results = await _client.SearchAsync(
                _collection,
                queryVector,
                limit: (ulong)limit,
                payloadSelector: false, // we only need IDs and scores
                cancellationToken: ct);

            var hits = results
                .Select(r => (
                    ChunkId: Guid.Parse(r.Id.Uuid),
                    Score: r.Score))
                .ToList();

            _log.LogInformation(
                "Qdrant search returned {Count} hits (limit={Limit}).",
                hits.Count, limit);

            return hits;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to search Qdrant collection '{Collection}'.", _collection);
            throw;
        }
    }

    // ── 4. DeleteDocumentAsync ────────────────────────────────────────────────

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        try
        {
            // Delete all chunks where payload.document_id matches
            await _client.DeleteAsync(
                _collection,
                filter: new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "document_id",
                                Match = new Match { Text = documentId.ToString() }
                            }
                        }
                    }
                },
                cancellationToken: ct);

            _log.LogInformation(
                "Deleted all chunks for document '{DocumentId}' from Qdrant.",
                documentId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to delete document '{DocumentId}' from Qdrant.", documentId);
            throw;
        }
    }
}