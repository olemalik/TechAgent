
using OilGasAI.API.Models;

namespace OilGasAI.API.Services;

public interface IOllamaONGService
{
    Task<ChatResponse> ChatAsync(ChatRequest request);
    Task<bool> IsModelAvailableAsync();
}