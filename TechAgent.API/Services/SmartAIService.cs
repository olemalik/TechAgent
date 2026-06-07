public class SmartAIService
{
    private readonly OllamaService _ollama;
    private readonly ClaudeService _claude;
    private readonly ILogger<SmartAIService> _logger;

    public SmartAIService(
        OllamaService ollama,
        ClaudeService claude,
        ILogger<SmartAIService> logger)
    {
        _ollama = ollama;
        _claude = claude;
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
            _logger.LogWarning(ex, "Ollama failed. Falling back to Claude...");
            return _claude.Summarize(news);
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
            _logger.LogWarning(ex, "Ollama failed. Falling back to Claude...");
            return _claude.ChatSummarize(news);
        }
    }
}