// Services/AIService.cs
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OilGasAI.API.Interfaces;

namespace OilGasAI.API.Services;

public class AIService : IAIService
{
    private readonly IChatClient _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<AIService> _logger;

    public AIService(
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<AIService> logger)
    {
        _chatClient = chatClient;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public async Task<string> ChatAsync(string message, CancellationToken ct = default)
        => await ChatAsync([new ChatMessage(ChatRole.User, message)], null, ct);

    public async Task<string> ChatAsync(
        IList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, options, ct);
            return response.Messages.LastOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling AI chat");
            throw;
        }
    }

    public IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default)
        => StreamAsync([new ChatMessage(ChatRole.User, prompt)], null, ct);

    public async IAsyncEnumerable<string> StreamAsync(
        IList<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, ct))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var embedding = await _embeddingGenerator.GenerateAsync(text, cancellationToken: ct);
            return embedding.Vector.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding");
            throw;
        }
    }
}