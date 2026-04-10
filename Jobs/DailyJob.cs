using System.Text;

public class DailyJob
{
    private readonly NewsService _news;
    private readonly SmartAIService _ai;

    private readonly TelegramService _telegram;

    public DailyJob(
        NewsService news,
        SmartAIService ai,
        TelegramService telegram)
    {
        _news = news;
        _ai = ai;
        _telegram = telegram;
    }

    public async Task Run()
    {
        var news = await _news.GetNews();
        var builder = new StringBuilder();

        await foreach (var chunk in _ai.Summarize(news))
        {
            builder.Append(chunk);
        }

        var fullResponse = builder.ToString();
        await _telegram.Send(fullResponse);
        /*
        var summary = await _ai.Summarize(news);

        await _telegram.Send(summary);*/


    }

}