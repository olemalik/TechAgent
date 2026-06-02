using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Services;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddScoped<NewsService>();
builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddHttpClient<IOllamaONGService, OlamaONGService>();
builder.Services.AddTransient<TelegramService>();
builder.Services.AddTransient<DailyJob>();

builder.Services.AddHangfire(x => x.UseMemoryStorage());
builder.Services.AddHangfireServer();
//builder.Services.AddSingleton<OpenAIService>();
// SmartAIService should be transient to avoid lifetime issues with typed HttpClient
builder.Services.AddTransient<ClaudeService>();
builder.Services.AddTransient<SmartAIService>();

// ─── NEW: Qdrant vector database client(singleton — thread-safe, gRPC port 6334) ──
builder.Services.AddSingleton(_ => new QdrantClient(
    host: builder.Configuration["Qdrant:Host"] ?? "localhost",
    port: int.Parse(builder.Configuration["Qdrant:Port"] ?? "6334")));

// ─── NEW: Typed HttpClient to Ollama /api/generate (named client for RagService) ──
// Separate from OllamaService HttpClient above — different timeout (generation is slow on CPU)
builder.Services.AddHttpClient("ollama-generate", c =>
{
    c.Timeout = TimeSpan.FromMinutes(10);  // CPU inference can take up to 60s per response
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
builder.Services.AddScoped<DocumentIngestionService>();
builder.Services.AddScoped<IDocumentIngestionService>(sp =>
    sp.GetRequiredService<DocumentIngestionService>());

// Background service: processes document ingestion queue items off the HTTP thread
builder.Services.AddHostedService<IngestionBackgroundService>();

// ─── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddControllers();

var app = builder.Build();

// Ensure Qdrant collection exists at startup (idempotent — safe to call every time)
await using (var scope = app.Services.CreateAsyncScope())
{
    var qdrant = scope.ServiceProvider.GetRequiredService<IQdrantService>();
    await qdrant.EnsureCollectionAsync();
}

app.UseCors("AllowAngular");
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