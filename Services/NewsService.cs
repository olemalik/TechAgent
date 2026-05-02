using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Options;

public class NewsService
{
    private readonly AppDbContext _db;
    private readonly NewsSettings _newsSettings;
    private readonly OllamaService _ollama;
    private readonly SmartAIService _aiFallback;

    public NewsService(AppDbContext db, IOptions<AppSettings> appSettings, OllamaService ollama, SmartAIService aiFallback)
    {
        _db = db;
        _newsSettings = appSettings.Value.NewsSettings ?? new NewsSettings();
        _ollama = ollama;
        _aiFallback = aiFallback;
    }

    public async Task<List<string>> GetNews()
    {
        var result = new List<string>();
        var feeds = _newsSettings.Feeds ?? Array.Empty<string>();
        var keywords = _newsSettings.Keywords ?? Array.Empty<string>();
        if (!feeds.Any() || !keywords.Any())
            return result;
        try
        {
            foreach (var url in feeds)
            {
                using var reader = XmlReader.Create(url);
                var feed = SyndicationFeed.Load(reader);


                var items = feed.Items
                    .Where(x => x.PublishDate.UtcDateTime > DateTime.UtcNow.AddDays(-1))
                    .Select(x => new
                    {
                        Title = x.Title.Text,
                        Link = x.Links.FirstOrDefault()?.Uri.ToString()
                    });

                foreach (var item in items)
                {
                    // 🔍 Keyword filtering
                    var matchedTags = keywords
                        .Where(k => item.Title.Contains(k, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    bool exists = _db.SentNews.Any(x => x.Title == item.Title);

                    if (!exists)
                    {
                        var tagsString = string.Join(",", matchedTags);

                        result.Add($"{item.Title} - {item.Link} [{tagsString}]");

                        _db.SentNews.Add(new SentNews
                        {
                            Title = item.Title,
                            Link = item.Link,
                            Hash = tagsString,
                            SentDate = DateTime.UtcNow
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // 🔥 DO NOT crash — just log and continue
            Console.WriteLine($"Feed failed: {feeds?.FirstOrDefault()?.ToString() ?? "unknown feed"}");
            Console.WriteLine(ex.Message);
        }
        return result.Take(5).ToList();
    }

    // Build a ready-to-send message: summarise news via Ollama (with fallback), and append source links
    public async Task<string> PrepareDailyMessageAsync()
    {
        var news = await GetNews();
        if (!news.Any())
            return string.Empty;

        var builder = new System.Text.StringBuilder();

        bool usedOllama = false;
        try
        {
            await foreach (var chunk in _ollama.Summarize(news))
            {
                builder.Append(chunk);
            }
            usedOllama = true;
        }
        catch
        {
            // fallback to SmartAIService
        }

        if (!usedOllama)
        {
            await foreach (var chunk in _aiFallback.Summarize(news))
            {
                builder.Append(chunk);
            }
        }

        var fullResponse = builder.ToString();

        var sources = news
            .Select(n =>
            {
                var idx = n.IndexOf(" - ");
                if (idx < 0) return string.Empty;
                var linkPart = n.Substring(idx + 3).Trim();
                var tagIdx = linkPart.IndexOf("[");
                if (tagIdx >= 0) linkPart = linkPart.Substring(0, tagIdx).Trim();
                return linkPart;
            })
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();

        var message = fullResponse;
        if (sources.Any())
        {
            message += "\n\nSources:\n" + string.Join("\n", sources);
        }

        return message;
    }
}