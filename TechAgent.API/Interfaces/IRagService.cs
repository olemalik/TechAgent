using OilGasAI.API.Models;

namespace OilGasAI.API.Interfaces;

public interface IRagService
{
    Task<RagChatResponse> AskAsync(RagChatRequest request, CancellationToken ct = default);
}