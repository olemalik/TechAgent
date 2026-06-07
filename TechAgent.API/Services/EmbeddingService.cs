using System.Net.Http.Json;
using OilGasAI.API.Models;

namespace OilGasAI.API.Services;

/// <summary>
/// Calls Ollama /api/embed using the nomic-embed-text model.
///
/// VERIFIED FACTS about nomic-embed-text:
///   - Always produces 768 numbers per piece of text (fixed dimension).
///   - Numbers are L2-normalised (already scaled to length 1.0).
///   - MUST use task prefixes for best results:
///       "search_document: [text]" when embedding a chunk for storage.
///       "search_query: [text]"    when embedding a user question for search.
///   - Ollama endpoint: POST /api/embed
///     Body: { "model": "nomic-embed-text", "input": ["text1", "text2"] }
///     Response: { "embeddings": [[num, num, ...], [...]] }
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<EmbeddingService> _log;
    private readonly string _model;

    public EmbeddingService(HttpClient http, IConfiguration config, ILogger<EmbeddingService> log)
    {
        _http = http;
        _config = config;
        _log = log;
        _model = config["Ollama:EmbeddingModel"] ?? "nomic-embed-text";
        _http.BaseAddress = new Uri(config["Ollama:BaseUrl"] ?? "http://localhost:11434");
        _http.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => (await EmbedBatchAsync([text], ct))[0];

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var request = new OllamaEmbedRequest(_model, texts);
        using var response = await _http.PostAsJsonAsync("/api/embed", request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct)
                   ?? throw new InvalidOperationException("Empty embedding response from Ollama.");

        // Verify dimension — nomic-embed-text must return 768. Any other number
        // means the wrong model is configured and Qdrant collection will be mismatched.
        if (body.embeddings[0].Length != 768)
        {
            _log.LogError("Wrong embedding dimension: {Dim}. Expected 768 from nomic-embed-text.", body.embeddings[0].Length);
            throw new InvalidOperationException(
                $"Expected 768-dim embeddings, got {body.embeddings[0].Length}. " +
                "Check Ollama:EmbeddingModel in appsettings.json.");
        }

        return body.embeddings;
    }
}