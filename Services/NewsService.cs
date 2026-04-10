using System.ServiceModel.Syndication;
using System.Xml;

public class NewsService
{
    private readonly string[] feeds =
    {
        "https://feeds.feedburner.com/TechCrunch/",
        "https://www.theverge.com/rss/index.xml"
    };

    public async Task<List<string>> GetNews()
    {
        var result = new List<string>();

        foreach (var url in feeds)
        {
            using var reader = XmlReader.Create(url);
            var feed = SyndicationFeed.Load(reader);

            var items = feed.Items.Take(5)
                .Select(x => $"{x.Title.Text} - {x.Links.FirstOrDefault()?.Uri}");

            result.AddRange(items);
        }

        // 🔥 Pre-filter (important)
        var keywords = new[] { ".NET", "Angular", "AI", "Azure", "SQL", "C#" };

        return result
            .Where(n => keywords.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToList();
    }
}