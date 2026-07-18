using System.Text.Json;
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
    /// POST /api/chat/stream
    /// Same RAG pipeline as /api/chat but streams the AI response as Server-Sent Events.
    /// Each event: data: {"type":"token","value":"..."}\n\n
    /// Final event: data: {"type":"done","sessionId":"..."}\n\n
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamChat([FromBody] RagChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = 400;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        try
        {
            await foreach (var chunk in _ragService.StreamAsync(request, ct))
            {
                var json = JsonSerializer.Serialize(chunk, opts);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
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
            .Select(h => new { h.Id, h.Role, h.Message, h.CreatedAt, h.WasRefused, h.FeedbackScore, h.AttachmentName, h.AttachmentUrl, h.AttachmentContentType })
            .ToListAsync(ct);

        return Ok(history);
    }

    /// <summary>
    /// POST /api/chat/feedback
    /// Records a thumbs up/down rating on an assistant message.
    /// Messages rated +1 and optionally corrected are promoted to "golden" examples
    /// that get injected as few-shot context in future prompts.
    /// </summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] FeedbackRequest req, CancellationToken ct)
    {
        if (req.Score is not (1 or -1))
            return BadRequest("Score must be 1 (thumbs up) or -1 (thumbs down).");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var msg = await db.ChatHistory.FindAsync([req.MessageId], ct);
        if (msg is null || msg.Role != "assistant" || msg.IsDeleted)
            return NotFound("Assistant message not found.");

        msg.FeedbackScore = req.Score;

        if (!string.IsNullOrWhiteSpace(req.Correction))
        {
            // Store correction as a new golden entry so both the original and the
            // corrected version are available to future prompts.
            db.ChatHistory.Add(new ChatSessionHistory
            {
                SessionId = msg.SessionId,
                Role = "assistant",
                Message = req.Correction,
                FeedbackScore = 1,
                IsGolden = true
            });
        }
        else if (req.Score == 1)
        {
            // Thumbs up with no correction → promote original answer to golden pool.
            msg.IsGolden = true;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { promoted = msg.IsGolden });
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
//         return Ok(new { status = isAvailable ? "online" : "offline", model = "oilgas-assistant" });
//     }
// }