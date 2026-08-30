using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Interfaces;
using OilGasAI.API.Services;

namespace OilGasAI.API.Infrastructure;

public static class StartupExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations and initialises the Qdrant collection.
    /// Qdrant failure is non-fatal — the API starts without it and RAG features
    /// are degraded until Qdrant becomes reachable again.
    /// </summary>
    public static async Task InitialiseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        // ── Database migrations ───────────────────────────────────────────────
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // ── Qdrant collection ─────────────────────────────────────────────────
        // Non-fatal: if Qdrant is offline the API still starts; RAG calls will
        // fail individually until the vector store is reachable again.
        try
        {
            var qdrant = services.GetRequiredService<IQdrantService>();
            await qdrant.EnsureCollectionAsync();
        }
        catch (Exception ex)
        {
            var log = services.GetRequiredService<ILogger<Program>>();
            log.LogWarning(ex, "Qdrant unavailable at startup — RAG features are degraded until Qdrant is reachable.");
        }
    }
}