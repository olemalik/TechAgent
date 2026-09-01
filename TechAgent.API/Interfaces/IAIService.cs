using Microsoft.Extensions.AI;

namespace OilGasAI.API.Interfaces;

public interface IAIService
{
    Task<string> ChatAsync(string prompt, CancellationToken ct = default);

    /// <summary>Chat with explicit messages and options (used for agentic MCP tool calls).</summary>
    Task<string> ChatAsync(IList<ChatMessage> messages, ChatOptions? options, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default);

    /// <summary>Stream with explicit messages and options (used for agentic MCP tool calls).</summary>
    IAsyncEnumerable<string> StreamAsync(IList<ChatMessage> messages, ChatOptions? options, CancellationToken ct = default);

    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}