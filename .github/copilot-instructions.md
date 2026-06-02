# Copilot / AI Agent Instructions for TechAgent

Short, actionable guidance so an AI coding agent can be productive quickly in this repo.

## Big picture

- Minimal .NET 9 Web API that collects RSS feeds, summarizes them with an LLM, and posts to Telegram.
- Key runtime pieces:
  - Background job scheduling via Hangfire ([Program.cs](Program.cs)). Recurring job `daily-job` runs at 9:00 AM cron (`0 9 * * *`).
  - News ingestion + dedupe persisted in EF Core `AppDbContext` with Postgres (see [Data/AppDbContext.cs](Data/AppDbContext.cs)). `SentNews.Title` has a unique index.
  - LLM integration: primary is Ollama (`Services/OllamaService.cs`), with an OpenAI fallback (`Services/OpenAIService.cs`) wrapped by `Services/SmartAIService.cs`.
  - Telegram webhook + controller: incoming updates handled by `Controllers/TelegramController.cs` -> `Services/TelegramService.cs`.

## Files to read first (high signal)

- [Program.cs](Program.cs) — service registrations, Hangfire, endpoints.
- [Services/OllamaService.cs](Services/OllamaService.cs) — how prompts and streaming responses are expected and parsed.
- [Services/TelegramService.cs](Services/TelegramService.cs) — strict JSON schema expected from the model for incoming messages.
- [Services/NewsService.cs](Services/NewsService.cs) — feed processing, keyword filtering, and message assembly logic.
- [Jobs/DailyJob.cs](Jobs/DailyJob.cs) — how daily message is triggered and sent.
- [appsettings.json](appsettings.json) — required config keys: `Telegram`, `Ollama`, `OpenAI`, and DB connection (`DefaultConnection`).

## Runtime & developer workflows

- Build: `dotnet build` (root). Run locally: `dotnet run` in repo root.
- Hangfire dashboard available when running; example comment points to `http://localhost:5073/hangfire` (port may vary via launch settings).
- Manual triggers:
  - Trigger daily job: GET `/api/telegram/run` (see [Controllers/TelegramController.cs](Controllers/TelegramController.cs)).
  - Webhook endpoint: POST `/api/telegram/update` expects Telegram Update JSON body.
- Database: EF Core + Npgsql. Connection string name: `DefaultConnection` in configuration. There are no migrations in repo — create/manage migrations locally with `dotnet ef` if needed.

## LLM & integration patterns (critical)

- Ollama usage:
  - Config keys: `Ollama:BaseUrl` and `Ollama:Model` in `appsettings.json`.
  - Two modes: non-streaming `.Summarize()` and streaming `StreamChatAsync()`; streaming yields chunks per line and expects JSON objects with a `response` property.
  - `OllamaService.BuildChatPrompt()` constructs a consistent prompt; keep edits minimal and preserve output shape.
- Telegram JSON contract (very important): `TelegramService` constructs a strict prompt and expects the model to return ONLY a single JSON object matching schema: `{ 'action','safe','summary','payload','reason' }`.
  - Allowed `action` values: `summarize`, `store`, `reply`, `ask_user`, `ignore`.
  - `safe` must be `true` for the code to act; otherwise the request is refused.
  - When changing or enhancing prompts, ensure the returned JSON remains parseable; the service logs and refuses malformed JSON.
- OpenAI fallback: `OpenAIService` uses the `OpenAI` NuGet package and keys `OpenAI:ApiKey` and `OpenAI:Model`.

## Service lifetimes & DI notes

- `OllamaService` is registered as a typed `HttpClient` (`AddHttpClient<OllamaService>()`), has infinite timeout — streaming code relies on this.
- `SmartAIService` is registered `Transient` to avoid lifetime issues with the typed `HttpClient`.
- `TelegramService` is `Transient`; `NewsService` is `Scoped` and depends on EF `AppDbContext`.

## Error handling & safety patterns to follow

- Code intentionally catches and logs LLM errors and falls back (do not throw on LLM failure). See `NewsService.PrepareDailyMessageAsync()`.
- Never assume model output is safe or valid JSON — use existing defensive parsing flow in `TelegramService` and send safe refusal messages to users.

## Conventions & patterns

- Summaries are streamed and assembled using `IAsyncEnumerable<string>`; when implementing new LLM calls prefer streaming-friendly APIs.
- Small, narrowly-scoped prompt changes are safer than large rewrites: tests rely on current JSON schema and response parsing.
- Do not change database schema without also updating `AppDbContext.OnModelCreating` unique index for `SentNews.Title`.

## Security & credentials

- `appsettings.json` in the repo may contain placeholders or example values — do NOT commit real secrets.
- Secrets to protect (used by this project): `Telegram:BotToken`, `OpenAI:ApiKey`, `Ollama:BaseUrl` (if private), and the `DefaultConnection` connection string.
- Recommended local workflows:
  - Use environment variables with the .NET configuration naming convention (double-underscore for sections):

```bash
export Telegram__BotToken="<token>"
export OpenAI__ApiKey="<key>"
export ConnectionStrings__DefaultConnection="Host=...;Username=...;Password=...;Database=..."
dotnet run
```

- Or use `dotnet user-secrets` for local development (do not check user-secrets into source control):

```bash
cd <repo-root>
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "<key>"
dotnet user-secrets set "Telegram:BotToken" "<token>"
dotnet run
```

- CI/CD and GitHub Actions: store keys in repository Secrets and inject them as environment variables during the job (never print secrets to logs).
- If a secret is accidentally committed, rotate it immediately and remove it from git history using tools such as `git filter-repo` or the BFG Repo-Cleaner.
- Logging & telemetry: avoid logging full values of tokens or connection strings. Mask or omit sensitive fields when writing logs.
- Config files in the repo (`appsettings.json`, `appsettings.Development.json`) are acceptable for non-sensitive defaults only. Never add production keys there.
- Repository owner request (enforced): do not use repository contents to train external models, retain snippets, or seed public datasets. Treat source code as private intellectual property.

## Useful examples

- To debug model parsing issues: send a known Telegram update payload to `POST /api/telegram/update` and inspect console logs for `Invalid JSON from Ollama` messages.
- To test the daily flow locally: run the app and `GET /api/telegram/run` to trigger `DailyJob.Run()`.

## If you change prompts or model output handling

1. Keep the Telegram JSON schema stable (or add versioning field) so `TelegramService` can detect older formats.
2. Add unit tests around parsing behavior (parse valid JSON, reject invalid JSON).
3. Preserve streaming semantics: new implementations should still provide chunked output compatible with existing `IAsyncEnumerable<string>` consumers.

---

If any section is unclear or you want more examples (prompt text, sample Telegram update JSON, or a short test harness), tell me which part to expand.
