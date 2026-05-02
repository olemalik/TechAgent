using System.Text;

public class DailyJob
{
    private readonly NewsService _news;
    private readonly TelegramService _telegram;

    public DailyJob(
        NewsService news,
        TelegramService telegram)
    {
        _news = news;
        _telegram = telegram;
    }

    public async Task Run()
    {
        var message = await _news.PrepareDailyMessageAsync();
        if (string.IsNullOrWhiteSpace(message))
        {
            await _telegram.Send("No new relevant tech updates today 👍");
            return;
        }

        await _telegram.Send(message);
    }

}