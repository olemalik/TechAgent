using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Models;

namespace OilGasAI.API.Controllers;

[ApiController]
[Route("api/mcp-configs")]
public class McpConfigController : ControllerBase
{
    private readonly AppDbContext _db;

    public McpConfigController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _db.McpServerConfigs.OrderBy(x => x.Name).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] McpServerConfig config, CancellationToken ct)
    {
        config.Id        = Guid.NewGuid();
        config.CreatedAt = DateTimeOffset.UtcNow;
        _db.McpServerConfigs.Add(config);
        await _db.SaveChangesAsync(ct);
        return Ok(config);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] McpServerConfig config, CancellationToken ct)
    {
        var existing = await _db.McpServerConfigs.FindAsync([id], ct);
        if (existing is null) return NotFound();

        existing.Name          = config.Name;
        existing.TransportType = config.TransportType;
        existing.Url           = config.Url;
        existing.ApiKey        = config.ApiKey;
        existing.Description   = config.Description;
        existing.IsEnabled     = config.IsEnabled;

        await _db.SaveChangesAsync(ct);
        return Ok(existing);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var existing = await _db.McpServerConfigs.FindAsync([id], ct);
        if (existing is null) return NotFound();
        _db.McpServerConfigs.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        var config = await _db.McpServerConfigs.FindAsync([id], ct);
        if (config is null) return NotFound();
        if (string.IsNullOrWhiteSpace(config.Url))
            return Ok(new { reachable = false, error = "No URL configured." });

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

            if (!string.IsNullOrEmpty(config.ApiKey))
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

            // SSE servers expect an Accept header; HTTP servers expect a POST.
            // We do a lightweight GET first — many MCP servers return 4xx for plain
            // GETs (they require the MCP handshake), so we treat those as "reachable"
            // rather than "offline".
            HttpResponseMessage res;
            if (config.TransportType == "sse")
            {
                http.DefaultRequestHeaders.Add("Accept", "text/event-stream");
                res = await http.GetAsync(config.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            else if (config.TransportType == "http")
            {
                // Send a minimal JSON-RPC initialize probe
                var probe = new StringContent(
                    """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"TechAgent","version":"1.0"}}}""",
                    System.Text.Encoding.UTF8, "application/json");
                res = await http.PostAsync(config.Url, probe, ct);
            }
            else
            {
                res = await http.GetAsync(config.Url, ct);
            }

            var status = (int)res.StatusCode;

            // 4xx from the MCP endpoint itself means the server is up but requires
            // proper MCP client negotiation — treat as reachable.
            var reachable = status < 500;
            var note = status switch
            {
                200 or 101 => null,
                404 => "Server reachable — MCP endpoint requires proper client handshake (this is normal).",
                405 => "Server reachable — method not allowed for plain HTTP probe (this is normal for SSE).",
                401 or 403 => "Server reachable — authentication required.",
                _ => $"HTTP {status} — server responded."
            };

            return Ok(new { reachable, status, note });
        }
        catch (TaskCanceledException)
        {
            return Ok(new { reachable = false, error = "Connection timed out." });
        }
        catch (HttpRequestException ex)
        {
            return Ok(new { reachable = false, error = $"Could not connect: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return Ok(new { reachable = false, error = ex.Message });
        }
    }
}