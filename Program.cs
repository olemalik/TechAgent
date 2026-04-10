using Hangfire;
using Hangfire.MemoryStorage;

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddOpenApi();

builder.Services.AddHttpClient();

builder.Services.AddSingleton<NewsService>();
builder.Services.AddSingleton<OllamaService>();
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddTransient<DailyJob>();

builder.Services.AddHangfire(x => x.UseMemoryStorage());
builder.Services.AddHangfireServer();
builder.Services.AddSingleton<OllamaService>();
//builder.Services.AddSingleton<OpenAIService>();
builder.Services.AddSingleton<SmartAIService>();


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

//Hangfire Dashboard : http://localhost:5049/hangfire
// to get the ChatID : https://api.telegram.org/bot8695096801:AAEaPBZk31Jo17ryTOGXyAzdPCZadMLIho8/getUpdates
