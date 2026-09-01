using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OilGasAI.API.Infrastructure;
using OilGasAI.API.Interfaces;
using OilGasAI.API.Services;
using OpenAI;
using Qdrant.Client;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);
var provider = builder.Configuration["AI:Provider"]; // "Ollama", "OpenAI", "Claude"

switch (provider)
{
    case "Ollama":
        var ollamaClient = new OpenAIClient(
            new ApiKeyCredential("ollama"),
            new OpenAIClientOptions { Endpoint = new Uri(builder.Configuration["AI:Ollama:Endpoint"]! + "/v1") }
        );
        builder.Services.AddChatClient(
            ollamaClient.GetChatClient(builder.Configuration["AI:Ollama:ChatModel"]!).AsIChatClient()
        ).UseFunctionInvocation();  // agentic loop: model calls a tool → execute → feed result back → repeat
        builder.Services.AddEmbeddingGenerator(
            ollamaClient.GetEmbeddingClient(builder.Configuration["AI:Ollama:EmbeddingModel"]!).AsIEmbeddingGenerator()
        );
        break;

    case "OpenAI":
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(builder.Configuration["AI:OpenAI:ApiKey"]!)
        );
        builder.Services.AddChatClient(
            openAIClient.GetChatClient(builder.Configuration["AI:OpenAI:ChatModel"]!).AsIChatClient()
        ).UseFunctionInvocation();
        builder.Services.AddEmbeddingGenerator(
            openAIClient.GetEmbeddingClient(builder.Configuration["AI:OpenAI:EmbeddingModel"]!).AsIEmbeddingGenerator()
        );
        break;

    case "Claude":
        var claudeClient = new OpenAIClient(
            new ApiKeyCredential(builder.Configuration["AI:Claude:ApiKey"]!),
            new OpenAIClientOptions { Endpoint = new Uri("https://api.anthropic.com/v1") }
        );
        builder.Services.AddChatClient(
            claudeClient.GetChatClient(builder.Configuration["AI:Claude:ChatModel"]!).AsIChatClient()
        ).UseFunctionInvocation();
        break;
}

// Register unified AI service
builder.Services.AddScoped<IAIService, AIService>();

builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection("AppSettings"));
// ─── PostgreSQL + EF Core (existing + new tables) ────────────────────────────
// AddPooledDbContextFactory: reuses context instances → better perf for high-request loads.
// The scoped DbContext line below lets controllers/services inject AppDbContext directly.
//uilder.Services.AddDbContext<AppDbContext>
builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

//builder.Services.AddOpenApi();

builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

builder.Services.AddHttpClient();

// ── MCP: named client used by McpToolRegistry for server connections ───────
builder.Services.AddHttpClient("mcp", c => c.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddScoped<NewsService>();
builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddTransient<TelegramService>();
builder.Services.AddTransient<DailyJob>();

builder.Services.AddHangfire(x => x.UseMemoryStorage());
builder.Services.AddHangfireServer();

// ─── NEW: Typed HttpClient to Ollama /api/generate (named client for RagService) ──
// Separate from OllamaService HttpClient above — different timeout (generation is slow on CPU)
builder.Services.AddHttpClient("ollama-generate", c =>
{
    c.Timeout = TimeSpan.FromMinutes(10);  // CPU inference can take up to 10minutes per response
});

// ─── NEW: HybridCache (.NET 9 GA) ─────────────────────────────────────────────
// Caches identical prompt→answer pairs in memory (stampede-safe).
// Drop-in Redis L2: just add AddStackExchangeRedisCache() and HybridCache detects it.
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new() { Expiration = TimeSpan.FromMinutes(30) };
});

// ─── NEW: RAG services ────────────────────────────────────────────────────────
// Singleton: DomainGuardService builds centroid once at first use and reuses it.
// Singleton: DocumentIngestionQueue is the thread-safe channel.
// Scoped: everything that touches the DB context must be scoped (not singleton).
builder.Services.AddSingleton<IDomainGuardService, DomainGuardService>();
builder.Services.AddSingleton<DocumentIngestionQueue>();

builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(3);
});

builder.Services.AddScoped<IQdrantService, QdrantService>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddScoped<EvalService>();
builder.Services.AddScoped<DocumentIngestionService>();
builder.Services.AddScoped<IDocumentIngestionService>(sp =>
    sp.GetRequiredService<DocumentIngestionService>());

// Background service: processes document ingestion queue items off the HTTP thread
builder.Services.AddHostedService<IngestionBackgroundService>();

// ── MCP Tool Registry ─────────────────────────────────────────────────────
// Singleton: maintains live connections to all enabled MCP servers; caches tool lists.
// IHostedService: loads tools at startup and refreshes every 5 minutes.
builder.Services.AddSingleton<McpToolRegistry>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<McpToolRegistry>());

// ─── CORS ─────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddControllers();

var app = builder.Build();

await app.InitialiseAsync();

app.UseCors("AllowAngular");

// Serve files uploaded via the chat file-attach button from wwwroot/uploads/
var uploadsDir = System.IO.Path.Combine(app.Environment.WebRootPath ?? System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
Directory.CreateDirectory(uploadsDir);
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHangfireDashboard();

// ⏰ Daily at 9 AM - Hangfire uses CRON syntax: "0 9 * * *" = minute=0, hour=9, every day/month/dow
RecurringJob.AddOrUpdate<DailyJob>(
    "daily-job",
    job => job.Run(),
    "0 9 * * *");

app.MapControllers();
app.UseHttpsRedirection();

app.Run();
//Hangfire Dashboard : http://localhost:5073/hangfire
// ─── Endpoints summary ───────────────────────────────────────────────────────
// POST   /api/chat           → RAG-powered Oil & Gas chat (uses uploaded company docs)
// POST   /api/chat/direct    → Direct O&G chat (no RAG context)
// GET    /api/chat/health    → Quick readiness check
// POST   /api/documents/upload → Upload PDF company document (returns 202 Accepted)
// GET    /api/documents/{id}/status → Check indexing progress
// GET    /api/documents      → List all documents
// GET    /api/health         → Full system health check (DB + Qdrant + Ollama)
// GET    /api/telegram/run   → Manually trigger daily news job
// POST   /api/telegram/update → Telegram webhook endpoint
// GET    /hangfire           → Hangfire dashboard
// GET    /openapi            → OpenAPI spec (dev only)


/*DefaultConnection": "Host=localhost;Port=5432;Database=smart_ai;Username=appUser;Password=appUser@123"*/