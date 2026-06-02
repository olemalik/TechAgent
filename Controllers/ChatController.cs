using Microsoft.AspNetCore.Mvc;
using OilGasAI.API.Models;
using OilGasAI.API.Services;

namespace OilGasAI.API.Controllers;
// This version upgrades the controller to use IRagService — the full RAG pipeline that includes:
//   - Document-aware answers (from uploaded company PDFs)
//   - Conversation history (last 8 turns)
//   - Domain guard (two-layer Oil & Gas restriction)
//   - HybridCache (identical questions answered instantly from memory)

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IRagService _ragService;

    public ChatController(IRagService ragService)
    {
        _ragService = ragService;
    }

    /// <summary>
    /// POST /api/chat
    /// Accepts an Oil &amp; Gas question and returns an AI-generated answer.
    /// If company documents have been uploaded, the answer is grounded in those documents.
    /// No streaming — full response returned at once (stream:false to Ollama).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] RagChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty.");

        if (request.Message.Length > 2000)
            return BadRequest("Message too long (max 2000 characters).");

        var response = await _ragService.AskAsync(request, ct);

        if (!response.IsSuccess)
            return StatusCode(500, new { error = response.Error });

        return Ok(response);
    }

    /// <summary>
    /// POST /api/chat/direct
    /// Direct Oil &amp; Gas chat without RAG document search.
    /// Useful when no company documents are uploaded yet.
    /// </summary>
    [HttpPost("direct")]
    public async Task<IActionResult> DirectChat([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty.");

        // Wrap as a RAG request with no history (will use model knowledge only if no docs indexed)
        var ragRequest = new RagChatRequest { Message = request.Message, History = request.History };
        var response = await _ragService.AskAsync(ragRequest, ct);
        return Ok(response);
    }

    /// <summary>
    /// GET /api/chat/health
    /// Quick check that the AI model is reachable.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        // Lightweight check — Ollama connectivity verified by the full HealthController
        return Ok(new { status = "ready", note = "Use /api/health for full system check." });
    }
}


// [ApiController]
// [Route("api/[controller]")]
// public class ChatController : ControllerBase
// {
//     private readonly IOllamaONGService _ollamaService;

//     public ChatController(IOllamaONGService ollamaService)
//     {
//         _ollamaService = ollamaService;
//     }

//     [HttpPost]
//     public async Task<IActionResult> Chat([FromBody] ChatRequest request)
//     {
//         if (string.IsNullOrWhiteSpace(request.Message))
//             return BadRequest("Message cannot be empty.");

//         var response = await _ollamaService.ChatAsync(request);

//         if (!response.IsSuccess)
//             return StatusCode(500, response.Error);

//         return Ok(response);
//     }

//     [HttpGet("health")]
//     public async Task<IActionResult> Health()
//     {
//         var isAvailable = await _ollamaService.IsModelAvailableAsync();
//         return Ok(new { status = isAvailable ? "online" : "offline", model = "oilgas-assistant" });
//     }
// }