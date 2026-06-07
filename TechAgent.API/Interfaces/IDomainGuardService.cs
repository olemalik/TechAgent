namespace OilGasAI.API.Services;

public interface IDomainGuardService
{
    /// <summary>
    /// Returns true if the message is Oil &amp; Gas related and should be processed.
    /// Pass queryEmbedding (already generated for RAG) to enable the semantic Layer 2
    /// check with zero extra Ollama calls.
    /// </summary>
    Task<bool> IsAllowedAsync(string message, float[]? queryEmbedding = null, CancellationToken ct = default);
}