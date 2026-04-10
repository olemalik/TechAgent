using OpenAI;
using OpenAI.Chat;

public class OpenAIService
{
    private readonly IConfiguration _config;

    public OpenAIService(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Summarizes news for daily feed using OpenAI
    /// </summary>
    public async Task<string> Summarize(List<string> news)
    {
        if (news == null || !news.Any())
            return "No major updates today. You're up to date 👍";

        // Create OpenAI client with API key
        var client = new OpenAIClient(apiKey: _config["OpenAI:ApiKey"]);

        // Build prompt
        string prompt = $@"
You are a senior software architect assistant.

User:
Malik Ahmed - Senior .NET & Angular Engineer

Task:
- Filter relevant news
- Explain why it matters
- Keep it short and concise

Return format:

🚀 Malik's Tech Briefing

🔥 Top Updates
- Title
👉 Why it matters

🧠 Trends

News:
{string.Join("\n", news)}
";

        // NEW SDK usage: GetChatClient and CompleteChatAsync
        var chatClient = client.GetChatClient(_config["OpenAI:Model"]);

        var response = await chatClient.CompleteChatAsync(
            new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage("You are a helpful AI assistant."),
                ChatMessage.CreateUserMessage(prompt)
            }
        );

        // Return text from first message
        return response.Value.Content.FirstOrDefault()?.Text ?? "Failed to summarize news.";
    }
}