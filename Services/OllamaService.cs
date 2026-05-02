using System.Text;
using System.Text.Json;

public class OllamaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public OllamaService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _config = config;
    }

    public async IAsyncEnumerable<string> Summarize(List<string> news)
    {
        if (news.Count == 0)
            yield return "No major updates today. You're up to date 👍";

        var prompt = BuildChatPrompt(news);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_config["Ollama:BaseUrl"]}/api/generate",
            new
            {
                model = _config["Ollama:Model"],
                prompt,
                stream = false
            });

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        yield return doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
    }

    public async IAsyncEnumerable<string> ChatSummarize(List<string> news)
    {
        string result = string.Empty;

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_config["Ollama:BaseUrl"]}/api/generate",
                new
                {
                    model = _config["Ollama:Model"],
                    prompt = BuildChatPrompt(news),
                    stream = false
                });

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            result = doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
        }
        catch
        {
            throw; // Let fallback handle
        }

        yield return result;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(List<string> news)
    {
        var request = new
        {
            model = "llama3",
            prompt = BuildChatPrompt(news),
            stream = true
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_config["Ollama:BaseUrl"]}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            string chunk = string.Empty;
            try
            {
                var json = JsonDocument.Parse(line);
                chunk = json?.RootElement.GetProperty("response").GetString();
            }
            catch
            {
                // Ignore malformed chunks
            }

            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
    }
    private string BuildChatPrompt(List<string> news)
    {
        return $@"
            You are a senior software architect assistant.

            User:
            Malik Ahmed - Senior .NET & Angular Engineer

            Task:
            - Filter relevant news
            - Explain why it matters
            - Keep it short

            Return:

            🚀 Malik's Tech Briefing

            🔥 Top Updates
            - Title
            👉 Why it matters

            🧠 Trends

            News:
            {string.Join("\n", news)}
            ";
    }
}