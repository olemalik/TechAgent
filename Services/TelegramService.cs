using System.Text.Json;
using Microsoft.Extensions.Configuration;

public class TelegramService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public TelegramService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _config = config;
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

            // TODO: customize handling (save to DB, trigger commands, etc.)
            // For now, send a simple acknowledgement
            var reply = $"Received: {text}";
            await SendTo(chatId, reply);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to handle Telegram update: {ex.Message}");
        }
    }
}