
namespace OilGasAI.API.Services;

public interface IEmbeddingService
{
    /// <summary>Converts one text string into a 768-number meaning fingerprint.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Converts many text strings at once — more efficient than one-by-one.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}