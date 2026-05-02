using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection("AppSettings"));

//builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient();

builder.Services.AddScoped<NewsService>();
builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddTransient<DailyJob>();

builder.Services.AddHangfire(x => x.UseMemoryStorage());
builder.Services.AddHangfireServer();
//builder.Services.AddSingleton<OpenAIService>();
// SmartAIService should be transient to avoid lifetime issues with typed HttpClient
builder.Services.AddTransient<SmartAIService>();


builder.Services.AddControllers();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHangfireDashboard();

// ⏰ Daily at 9 AM
RecurringJob.AddOrUpdate<DailyJob>(
    "daily-job",
    job => job.Run(),
    "0 9 * * *");

app.MapControllers();
app.UseHttpsRedirection();

app.Run();
//Hangfire Dashboard : http://localhost:5073/hangfire
