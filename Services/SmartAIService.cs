public class SmartAIService
{
    private readonly OllamaService _ollama;
    private readonly ILogger<SmartAIService> _logger;

    public SmartAIService(
        OllamaService ollama,
        ILogger<SmartAIService> logger)
    {
        _ollama = ollama;
        _logger = logger;
    }

    public IAsyncEnumerable<string> Summarize(List<string> news)
    {
        try
        {
            _logger.LogInformation("Using Ollama...");
            return _ollama.Summarize(news);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama failed. Switching to OpenAI...");
            return _ollama.StreamChatAsync(news);
        }
    }
    public IAsyncEnumerable<string> ChatSummarize(List<string> news)
    {
        try
        {
            _logger.LogInformation("Using Ollama...");
            return _ollama.ChatSummarize(news);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama failed. Switching to OpenAI...");
            return _ollama.StreamChatAsync(news);
        }
    }
}