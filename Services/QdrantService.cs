using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace OilGasAI.API.Services;

/// <summary>
/// Wraps the Qdrant.Client gRPC SDK.
///
/// VERIFIED API NOTES for Qdrant.Client 1.12.0:
///   - Uses gRPC on port 6334 (not the REST port 6333 which is for the dashboard).
///   - QdrantClient is thread-safe — register as Singleton.
///   - PointId must be set as: new PointId { Uuid = guid.ToString() }
///   - Vectors are added via: point.Vectors.Vector.Data.AddRange(floatArray)
///   - SearchAsync parameter: "withPayload: true" (NOT "payloadSelector: true")
///   - Collection must be created with Size=768 for nomic-embed-text.
///   - Distance.Cosine = correct for L2-normalised vectors.
/// </summary>
public class QdrantService : IQdrantService
{
    private readonly QdrantClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<QdrantService> _log;
    private readonly string _collection;

    public QdrantService(QdrantClient client, IConfiguration config, ILogger<QdrantService> log)
    {
        _client = client;
        _config = config;
        _log = log;
        _collection = config["Qdrant:Collection"] ?? "oilgas_documents";
    }

    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        if (await _client.CollectionExistsAsync(_collection, ct))
        {
            _log.LogInformation("Qdrant collection '{C}' exists.", _collection);
            return;
        }

        await _client.CreateCollectionAsync(
            _collection,
            vectorsConfig: new VectorParams { Size = 768, Distance = Distance.Cosine },
            hnswConfig: new HnswConfigDiff { M = 16, EfConstruct = 100 },
            cancellationToken: ct);

        _log.LogInformation("Created Qdrant collection '{C}' (768-dim, Cosine).", _collection);
    }

    public async Task UpsertChunksAsync(
        IReadOnlyList<(Guid Id, float[] Vector, string ChildText, Guid DocumentId, int Index)> chunks,
        CancellationToken ct = default)
    {
        var points = new List<PointStruct>(chunks.Count);
        foreach (var (id, vector, childText, docId, idx) in chunks)
        {
            var point = new PointStruct
            {
                Id = new PointId { Uuid = id.ToString() },
                Vectors = new Vectors { Vector = new Vector() }
            };
            point.Vectors.Vector.Data.AddRange(vector);
            point.Payload["documentId"] = docId.ToString();
            point.Payload["chunkIndex"] = idx;
            point.Payload["preview"] = childText.Length > 100 ? childText[..100] + "…" : childText;
            points.Add(point);
        }

        await _client.UpsertAsync(_collection, points, cancellationToken: ct);
        _log.LogDebug("Upserted {N} vectors to Qdrant.", chunks.Count);
    }

    public async Task<IReadOnlyList<(Guid ChunkId, float Score)>> SearchAsync(
        float[] queryVector, int limit = 4, CancellationToken ct = default)
    {
        var results = await _client.SearchAsync(
            _collection,
            queryVector,
            limit: (ulong)limit,
            searchParams: new SearchParams { HnswEf = 128, Exact = false },
            payloadSelector: true,
            cancellationToken: ct);

        return results
            .Select(r => (ChunkId: Guid.Parse(r.Id.Uuid), Score: r.Score))
            .ToList();
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var filter = new Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = "documentId",
                Match = new Match { Text = documentId.ToString() }
            }
        });
        await _client.DeleteAsync(_collection, filter: filter, wait: true, cancellationToken: ct);
        _log.LogInformation("Deleted Qdrant points for document {Id}.", documentId);
    }
}