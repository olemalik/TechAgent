namespace OilGasAI.API.Models;

public class McpServerConfig
{
    public Guid   Id            { get; set; } = Guid.NewGuid();
    public string Name          { get; set; } = string.Empty;
    public string TransportType { get; set; } = "http";   // http | sse | stdio
    public string? Url          { get; set; }
    public string? ApiKey       { get; set; }
    public string? Description  { get; set; }
    public bool   IsEnabled     { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
