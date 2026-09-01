using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace OilGasAI.API.Services;

/// <summary>
/// Singleton hosted service that maintains live MCP server connections and caches their tools.
/// Tools are refreshed every 5 minutes. Individual server failures are non-fatal.
/// </summary>
public sealed class McpToolRegistry : IHostedService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpToolRegistry> _log;

    // Volatile for lock-free reads from request threads
    private volatile IReadOnlyList<AITool> _tools = [];
    private volatile IReadOnlyList<McpClient> _clients = [];
    private Timer? _refreshTimer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public McpToolRegistry(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<McpToolRegistry> log)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _log = log;
    }

    /// <summary>Returns the current cached set of tools from all connected MCP servers.</summary>
    public IReadOnlyList<AITool> GetTools() => _tools;

    public async Task StartAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        _refreshTimer = new Timer(
            _ => _ = RefreshAsync(CancellationToken.None),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    public Task StopAsync(CancellationToken ct)
    {
        _refreshTimer?.Dispose();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (!await _lock.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            _log.LogWarning("MCP registry: refresh skipped — previous refresh still running");
            return;
        }
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var configs = await db.McpServerConfigs
                .Where(c => c.IsEnabled && c.Url != null)
                .AsNoTracking()
                .ToListAsync(ct);

            var oldClients = _clients;
            var newClients = new List<McpClient>(configs.Count);
            var allTools   = new List<AITool>();

            foreach (var config in configs)
            {
                try
                {
                    var httpClient = _httpClientFactory.CreateClient("mcp");

                    if (!string.IsNullOrEmpty(config.ApiKey))
                        httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

                    var transportOptions = new HttpClientTransportOptions
                    {
                        Endpoint      = new Uri(config.Url!),
                        TransportMode = config.TransportType == "sse"
                            ? HttpTransportMode.Sse
                            : HttpTransportMode.AutoDetect
                    };

                    var transport = new HttpClientTransport(transportOptions, httpClient, _loggerFactory);
                    var client    = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: ct);
                    var tools     = await client.ListToolsAsync(cancellationToken: ct);

                    newClients.Add(client);
                    allTools.AddRange(tools);

                    _log.LogInformation(
                        "MCP '{Server}' connected — {Count} tool(s) loaded",
                        config.Name, tools.Count);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex,
                        "MCP '{Server}' ({Url}) — connection failed, server will be skipped",
                        config.Name, config.Url);
                }
            }

            // Atomic swap — callers always see a consistent snapshot
            _clients = newClients;
            _tools   = allTools;

            _log.LogInformation(
                "MCP registry refreshed: {Servers} server(s), {Tools} tool(s) total",
                newClients.Count, allTools.Count);

            foreach (var old in oldClients)
                await DisposeSafeAsync(old);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MCP registry refresh failed");
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task DisposeSafeAsync(McpClient client)
    {
        try { await client.DisposeAsync(); } catch { /* swallow — old client */ }
    }

    public async ValueTask DisposeAsync()
    {
        _refreshTimer?.Dispose();
        _lock.Dispose();
        foreach (var c in _clients)
            await DisposeSafeAsync(c);
    }
}
