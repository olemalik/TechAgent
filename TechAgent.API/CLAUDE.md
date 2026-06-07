# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Permission Rule

**ALWAYS ask the user for explicit permission before taking any of the following actions:**

- Editing any file
- Deleting any file or data
- Running or executing any command, script, or process
- Any other action that modifies state (database, config, git, etc.)

Wait for a clear confirmation (yes / no / ok / not ok) before proceeding. Do not assume consent from context alone.

## Commands

```bash
# Run the app
dotnet run

# Build
dotnet build

# Apply EF Core migrations
dotnet ef migrations add <MigrationName>
dotnet ef database update

# Hangfire dashboard (dev only)
# http://localhost:5073/hangfire

# Manually trigger the daily job via HTTP
GET /api/telegram/run
```

## Architecture

TechAgent is an ASP.NET Core 9 web app that runs a daily AI-powered tech news briefing delivered to Telegram. It also acts as a Telegram bot webhook, processing incoming messages with Ollama.

### Core Data Flow

**Daily briefing (scheduled at 9 AM via Hangfire):**
`DailyJob.Run()` → `NewsService.PrepareDailyMessageAsync()` → `OllamaService.Summarize()` (fallback: `SmartAIService`) → `TelegramService.Send()`

**Incoming Telegram message (webhook at `POST /api/telegram/update`):**
`TelegramController` → `TelegramService.HandleUpdateAsync()` → `OllamaService.ChatSummarize()` → structured JSON action dispatch (`summarize`, `reply`, `store`, `ask_user`, `ignore`)

### Key Design Decisions

- **Deduplication**: `NewsService` checks the `SentNews` table (unique index on `Title`) before adding an item. News is capped at 5 items per run.
- **Ollama-first AI**: All AI calls go to a local Ollama instance (`llama3` by default). `SmartAIService` is the fallback wrapper — it currently falls back to `OllamaService.StreamChatAsync()`, not OpenAI (`OpenAIService` exists but is commented out in `Program.cs`).
- **Telegram safety gate**: Incoming messages are sent to Ollama with a strict JSON-schema prompt. The bot only acts if `safe=true` in the response. Unsafe requests are refused and logged.
- **Hangfire in-memory**: Jobs are not persisted across restarts. The recurring job re-registers on every startup via `RecurringJob.AddOrUpdate`.

### Configuration

Config is split across `appsettings.json` and `appsettings.Development.json`. The following sections must be populated:

| Section | Keys |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Telegram` | `BotToken`, `ChatId` |
| `Ollama` | `BaseUrl` (default: `http://localhost:11434`), `Model` (default: `llama3`) |
| `AppSettings:NewsSettings` | `Feeds` (string array of RSS URLs), `Keywords` (string array for title filtering) |

News is only fetched for items published within the last 24 hours and whose title contains at least one keyword.

### Database

Single table: `SentNews` (`Id`, `Title` [unique], `Link`, `Hash` [stores matched keyword tags], `SentDate`). Managed via EF Core with Npgsql. Run `dotnet ef database update` after any schema changes.
