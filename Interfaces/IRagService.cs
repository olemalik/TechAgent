using OilGasAI.API.Models;

namespace OilGasAI.API.Services;

public interface IRagService
{
    Task<RagChatResponse> AskAsync(RagChatRequest request, CancellationToken ct = default);
}