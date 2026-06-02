using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Services;

namespace OilGasAI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IQdrantService _qdrant;
    private readonly IConfiguration _config;

    public HealthController(
        IDbContextFactory<AppDbContext> dbFactory,
        IQdrantService qdrant,
        IConfiguration config)
    {
        _dbFactory = dbFactory;
        _qdrant = qdrant;
        _config = config;
    }

    /// <summary>
    /// GET /api/health
    /// Checks all system components: PostgreSQL, Qdrant, Ollama.
    /// Returns "healthy" only when all are reachable.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        bool dbOk = false, qdrantOk = false, ollamaOk = false;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            dbOk = await db.Database.CanConnectAsync(ct);
        }
        catch { /* stays false */ }

        try
        {
            await _qdrant.EnsureCollectionAsync(ct);
            qdrantOk = true;
        }
        catch { /* stays false */ }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.GetAsync($"{_config["Ollama:BaseUrl"]}/api/tags", ct);
            ollamaOk = response.IsSuccessStatusCode;
        }
        catch { /* stays false */ }

        var status = dbOk && qdrantOk && ollamaOk ? "healthy" : "degraded";

        return Ok(new
        {
            status,
            components = new
            {
                postgresql = dbOk ? "ok" : "unreachable",
                qdrant = qdrantOk ? "ok" : "unreachable",
                ollama = ollamaOk ? "ok" : "unreachable"
            },
            models = new
            {
                chat = _config["Ollama:ONGModelName"] ?? "oilgas-assistant",
                embedding = _config["Ollama:EmbeddingModel"] ?? "nomic-embed-text"
            },
            timestamp = DateTimeOffset.UtcNow
        });
    }
}