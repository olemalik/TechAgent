using System.Text.Json;
using Microsoft.Extensions.Configuration;

public class TelegramService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly OllamaService _ollama;
    private readonly NewsService _newsService;

    public TelegramService(HttpClient http, IConfiguration config, OllamaService ollama, NewsService newsService)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _config = config;
        _ollama = ollama;
        _newsService = newsService;
    }

    // Send to configured chat id
    public async Task Send(string message)
    {
        var token = _config["Telegram:BotToken"];
        var chatId = _config["Telegram:ChatId"];

        var url = $"https://api.telegram.org/bot{token}/sendMessage";

        await _http.PostAsJsonAsync(url, new
        {
            chat_id = chatId,
            text = message
        });
    }

    // Send to a specific chat id (useful for replying to incoming messages)
    public async Task SendTo(long chatId, string message)
    {
        var token = _config["Telegram:BotToken"];
        var url = $"https://api.telegram.org/bot{token}/sendMessage";

        await _http.PostAsJsonAsync(url, new
        {
            chat_id = chatId,
            text = message
        });
    }

    // Handle incoming Telegram Update JSON (webhook)
    public async Task HandleUpdateAsync(JsonElement update)
    {
        try
        {
            if (!update.TryGetProperty("message", out var message))
                return; // ignore non-message updates for now

            string? text = null;
            long chatId = 0;

            if (message.TryGetProperty("text", out var textProp))
                text = textProp.GetString();

            if (message.TryGetProperty("chat", out var chatProp) && chatProp.TryGetProperty("id", out var idProp))
            {
                if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var id))
                    chatId = id;
            }

            if (chatId == 0 || string.IsNullOrWhiteSpace(text))
                return;

            // Build strict prompt asking Ollama to return ONLY JSON
            var prompt = $@"You MUST return ONLY a single JSON object and NOTHING else. Schema: {{'action','safe','summary','payload','reason'}}. Allowed actions: summarize, store, reply, ask_user, ignore. Set safe=false if the message requests illegal/harmful or system-level operations. Do NOT output instructions to run commands, access files, open network ports, or perform transactions. Input message: '{text.Replace("\"", "\\\"")}'";

            var sb = new System.Text.StringBuilder();
            try
            {
                await foreach (var chunk in _ollama.ChatSummarize(new List<string> { prompt }))
                {
                    sb.Append(chunk);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ollama call failed: {ex.Message}");
                await SendTo(chatId, "Sorry, I'm unable to process that right now.");
                return;
            }

            var jsonText = sb.ToString().Trim();

            // Try parse JSON
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                var action = root.GetProperty("action").GetString() ?? "ignore";
                var safe = root.GetProperty("safe").GetBoolean();
                var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty;

                if (!safe)
                {
                    await SendTo(chatId, "I cannot assist with that request.");
                    // log refusal
                    Console.WriteLine($"Refused unsafe request from {chatId}: {text}");
                    return;
                }

                switch (action)
                {
                    case "summarize":
                        if (!string.IsNullOrWhiteSpace(summary))
                            await SendTo(chatId, summary);
                        else
                            await SendTo(chatId, "No summary produced.");
                        break;
                    case "reply":
                        var payload = root.TryGetProperty("payload", out var p) ? p.GetString() ?? summary : summary;
                        await SendTo(chatId, payload);
                        break;
                    case "store":
                        // store the original message as a SentNews entry
                        await _newsService.AddSentNewsAsync(text, null, reason);
                        await SendTo(chatId, "Saved your message.");
                        break;
                    case "ask_user":
                        var q = !string.IsNullOrWhiteSpace(summary) ? summary : "Can you clarify?";
                        await SendTo(chatId, q);
                        break;
                    default:
                        // ignore or unknown action
                        Console.WriteLine($"Ignored action '{action}' from {chatId}");
                        break;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Model did not return valid JSON — refuse and log
                Console.WriteLine($"Invalid JSON from Ollama for message: {text}. Response: {jsonText}");
                await SendTo(chatId, "Sorry, I couldn't understand that response.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to handle Telegram update: {ex.Message}");
        }
    }
}