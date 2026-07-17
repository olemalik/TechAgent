namespace OilGasAI.API.Interfaces;

public interface IAIService
{
    Task<string> ChatAsync(string prompt, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default);
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}