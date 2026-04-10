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
}