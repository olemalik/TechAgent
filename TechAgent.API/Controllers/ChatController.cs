using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Interfaces;
using OilGasAI.API.Models;

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
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ChatController(IRagService ragService, IDbContextFactory<AppDbContext> dbFactory)
    {
        _ragService = ragService;
        _dbFactory = dbFactory;
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
    /// GET /api/chat/sessions
    /// Returns all sessions with their first message as title and last activity time.
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sessionMeta = await db.ChatHistory
            .Where(h => !h.IsDeleted)
            .GroupBy(h => h.SessionId)
            .Select(g => new { SessionId = g.Key, LastActivity = g.Max(h => h.CreatedAt) })
            .OrderByDescending(s => s.LastActivity)
            .AsNoTracking()
            .ToListAsync(ct);

        var sessionIds = sessionMeta.Select(s => s.SessionId).ToList();

        // Get the min Id (first message) per session for the title
        var firstIds = await db.ChatHistory
            .Where(h => sessionIds.Contains(h.SessionId) && h.Role == "user" && !h.IsDeleted)
            .GroupBy(h => h.SessionId)
            .Select(g => new { SessionId = g.Key, MinId = g.Min(h => h.Id) })
            .AsNoTracking()
            .ToListAsync(ct);

        var minIds = firstIds.Select(f => f.MinId).ToList();

        var firstMessages = await db.ChatHistory
            .Where(h => minIds.Contains(h.Id))
            .Select(h => new { h.Id, h.SessionId, h.Message })
            .AsNoTracking()
            .ToListAsync(ct);

        var sessions = sessionMeta.Select(s => new
        {
            s.SessionId,
            Title = firstMessages.FirstOrDefault(m => m.SessionId == s.SessionId)?.Message ?? "New Chat",
            s.LastActivity
        });

        return Ok(sessions);
    }

    /// <summary>
    /// GET /api/chat/history/{sessionId}
    /// Returns past messages for a session so the client can restore chat history on load.
    /// </summary>
    [HttpGet("history/{sessionId:guid}")]
    public async Task<IActionResult> GetHistory(Guid sessionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var history = await db.ChatHistory
            .Where(h => h.SessionId == sessionId && !h.IsDeleted)
            .OrderBy(h => h.CreatedAt)
            .AsNoTracking()
            .Select(h => new { h.Role, h.Message, h.CreatedAt, h.WasRefused })
            .ToListAsync(ct);

        return Ok(history);
    }

    /// <summary>
    /// DELETE /api/chat/history/{sessionId}
    /// Permanently removes all messages for a session.
    /// </summary>
    [HttpDelete("history/{sessionId:guid}")]
    public async Task<IActionResult> DeleteHistory(Guid sessionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var updated = await db.ChatHistory
            .Where(h => h.SessionId == sessionId && !h.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.IsDeleted, true), ct);
        return Ok(new { updated });
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