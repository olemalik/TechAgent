// Services/AIService.cs
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
    {
        try
        {
            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, message)],
                cancellationToken: ct);
            return response.Messages.LastOrDefault()?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling AI chat");
            throw;
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