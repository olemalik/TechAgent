using System.Text;
using System.Text.Json;
using OilGasAI.API.Models;

namespace OilGasAI.API.Services;

public class OlamaONGService : IOllamaONGService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OlamaONGService> _logger;
    private readonly string _baseUrl;
    private readonly string _modelName;

    // Domain guard keywords
    private static readonly string[] OilGasKeywords = {
        "oil", "gas", "petroleum", "drilling", "refinery", "pipeline",
        "upstream", "downstream", "midstream", "reservoir", "wellbore",
        "bop", "fpso", "lng", "lpg", "hydrocarbon", "fracking",
        "completion", "perforation", "separator", "compressor", "bopd",
        "gor", "wor", "hse", "api", "offshore", "onshore", "rig",
        "mud", "casing", "cementing", "production", "exploration"
    };

    private static readonly string[] OffTopicKeywords = {
        "recipe", "cooking", "sports", "movie", "music", "fashion",
        "politics", "religion", "dating", "gaming", "weather"
    };

    public OlamaONGService(HttpClient httpClient, IConfiguration config, ILogger<OlamaONGService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _modelName = config["Ollama:ONGModelName"] ?? "oilgas-assistant";
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        try
        {
            // Client-side domain guard
            if (IsOffTopic(request.Message))
            {
                return new ChatResponse
                {
                    IsSuccess = true,
                    Reply = "I'm specialized only in Oil & Gas industry topics. " +
                            "Please ask me something related to upstream, midstream, " +
                            "or downstream operations, HSE, drilling, or petroleum engineering."
                };
            }

            var ollamaRequest = new OllamaRequest
            {
                Model = _modelName,
                Stream = false,
                Messages = request.History
                    .Select(h => new OllamaMessage { Role = h.Role, Content = h.Content })
                    .Append(new OllamaMessage { Role = "user", Content = request.Message })
                    .ToList()
            };

            var json = JsonSerializer.Serialize(ollamaRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content);

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return new ChatResponse
            {
                IsSuccess = true,
                Reply = ollamaResponse?.Message?.Content ?? "No response received."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Ollama API");
            return new ChatResponse
            {
                IsSuccess = false,
                Error = "Failed to get response from AI model. Please ensure Ollama is running."
            };
        }
    }

    public async Task<bool> IsModelAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private bool IsOffTopic(string message)
    {
        var lower = message.ToLower();
        // If it contains obvious off-topic keywords and no oil/gas keywords, block it
        bool hasOffTopic = OffTopicKeywords.Any(k => lower.Contains(k));
        bool hasOilGas = OilGasKeywords.Any(k => lower.Contains(k));
        return hasOffTopic && !hasOilGas;
    }
}