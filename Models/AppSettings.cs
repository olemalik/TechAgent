public class AppSettings
{
    public NewsSettings? NewsSettings { get; set; }
}
public class NewsSettings
{
    public string[] Feeds { get; set; } = Array.Empty<string>();
    public string[] Keywords { get; set; } = Array.Empty<string>();
}