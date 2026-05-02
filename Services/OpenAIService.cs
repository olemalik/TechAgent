using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
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
    /// Summarizes a list of news lines using OpenAI and returns the generated text.
    /// </summary>
    public async Task<string> Summarize(List<string> news)
    {
        if (news == null || news.Count == 0)
            return "No major updates today. You're up to date 👍";

        try
        {
            var client = new OpenAIClient(apiKey: _config["OpenAI:ApiKey"]);

            var prompt = $"You are a senior software architect assistant.\n\n" +
                         $"User:\nMalik Ahmed - Senior .NET & Angular Engineer\n\n" +
                         $"Task:\n- Filter relevant news\n- Explain why it matters\n- Keep it short and concise\n\n" +
                         $"Return format:\n\n🚀 Malik's Tech Briefing\n\n🔥 Top Updates\n- Title\n👉 Why it matters\n\n🧠 Trends\n\nNews:\n{string.Join("\n", news)}";

            var chatClient = client.GetChatClient(_config["OpenAI:Model"]);

            var response = await chatClient.CompleteChatAsync(new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage("You are a helpful AI assistant."),
                ChatMessage.CreateUserMessage(prompt)
            });

            return response.Value.Content.FirstOrDefault()?.Text ?? "Failed to summarize news.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenAI summarize failed: {ex.Message}");
            return "No major updates today. (summary unavailable)";
        }
    }
}