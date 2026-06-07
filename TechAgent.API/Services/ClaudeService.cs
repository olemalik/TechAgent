using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

public class ClaudeService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ClaudeService> _logger;

    public ClaudeService(IConfiguration config, ILogger<ClaudeService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> Summarize(List<string> news)
    {
        if (news.Count == 0)
        {
            yield return "No major updates today. You're up to date.";
            yield break;
        }

        var client = new AnthropicClient(_config["Claude:ApiKey"] ?? string.Empty);

        var parameters = new MessageParameters
        {
            Model = AnthropicModels.Claude46Sonnet,
            MaxTokens = 1024,
            Messages = [new Message { Role = RoleType.User, Content = [new TextContent { Text = BuildPrompt(news) }] }]
        };

        var response = await client.Messages.GetClaudeMessageAsync(parameters);
        _logger.LogInformation("Claude Summarize used {tokens} tokens", response.Usage?.InputTokens);

        yield return ((TextContent)response.Content[0]).Text;
    }

    public async IAsyncEnumerable<string> ChatSummarize(List<string> news)
    {
        if (news.Count == 0)
        {
            yield return "No major updates today. You're up to date.";
            yield break;
        }

        var client = new AnthropicClient(_config["Claude:ApiKey"] ?? string.Empty);

        var parameters = new MessageParameters
        {
            Model = AnthropicModels.Claude46Sonnet,
            MaxTokens = 1024,
            Messages = [new Message { Role = RoleType.User, Content = [new TextContent { Text = BuildPrompt(news) }] }]
        };

        var response = await client.Messages.GetClaudeMessageAsync(parameters);
        _logger.LogInformation("Claude ChatSummarize used {tokens} tokens", response.Usage?.InputTokens);

        yield return ((TextContent)response.Content[0]).Text;
    }

    private string BuildPrompt(List<string> news) =>
        $"""
        You are a senior software architect assistant.

        User:
        Malik Ahmed - Senior .NET & Angular Engineer

        Task:
        - Filter relevant news
        - Explain why it matters
        - Keep it short

        Return:

        🚀 Malik's Tech Briefing

        🔥 Top Updates
        - Title
        👉 Why it matters

        🧠 Trends

        News:
        {string.Join("\n", news)}
        """;
}
