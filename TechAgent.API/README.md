# TechAgent.API

ASP.NET Core 9 backend — Oil & Gas AI chat (RAG + streaming + feedback loop) and daily tech news briefing via Telegram.

See the project root **[README.md](../README.md)** for full architecture, pipeline diagrams, and quick-start guide.

---

## Secrets Management

Secrets are **never stored in `appsettings.json`**. The approach differs between local development and Docker.

### Local Development (`dotnet run`)

Use .NET User Secrets. They live only on your machine (`~/.microsoft/usersecrets/`) and are never committed.

```bash
cd TechAgent.API
dotnet user-secrets set "Telegram:BotToken"   "your-bot-token"
dotnet user-secrets set "Telegram:ChatId"     "your-chat-id"
dotnet user-secrets set "OpenAI:ApiKey"       "your-openai-key"
dotnet user-secrets set "Claude:ApiKey"       "your-claude-key"

# Verify
dotnet user-secrets list
```

Set the database connection in `appsettings.Development.json` (already gitignored):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=techagent;Username=appUser;Password=appUser@123"
  }
}
```

### Docker (`docker-compose up`)

```bash
cp .env.example .env
# Edit .env with your real values — it is gitignored
```

`.env` file:

```env
TELEGRAM_BOT_TOKEN=your-real-token
TELEGRAM_CHAT_ID=your-real-chat-id
OPENAI_API_KEY=your-real-openai-key
CLAUDE_API_KEY=your-real-claude-key
```

`docker-compose.yml` reads these and injects them as container environment variables. ASP.NET Core maps `__` → `:` automatically (`Telegram__BotToken` → `Telegram:BotToken`).

### Summary

| Environment | Secret source |
| --- | --- |
| `dotnet run` (local) | `dotnet user-secrets` → `~/.microsoft/usersecrets/` |
| `docker-compose up` | `.env` → docker-compose env vars → container |

---

## Configuration Reference

| Key | Purpose | Secret? |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string | Yes |
| `Telegram:BotToken` | Telegram bot API token | Yes |
| `Telegram:ChatId` | Channel/chat to send briefings to | Yes |
| `OpenAI:ApiKey` | OpenAI API key (optional fallback) | Yes |
| `Claude:ApiKey` | Anthropic API key (fallback AI) | Yes |
| `Ollama:BaseUrl` | Ollama server (default `http://localhost:11434`) | No |
| `Ollama:ONGModelName` | O&G chat model (default `oilgas-assistant`) | No |
| `Ollama:EmbeddingModel` | Embedding model (default `nomic-embed-text`) | No |
| `Qdrant:Host` / `Qdrant:Port` | Qdrant gRPC connection (default `localhost:6334`) | No |
| `Qdrant:Collection` | Collection name (default `oilgas_documents`) | No |
| `AppSettings:NewsSettings:Feeds` | RSS feed URLs | No |
| `AppSettings:NewsSettings:Keywords` | Title-filter keywords for news | No |

---

## Commands

```bash
# Run the API
dotnet run

# Build
dotnet build

# EF Core migrations (migrations live in Data/Migrations/)
dotnet ef migrations add <MigrationName> -o Data/Migrations
dotnet ef database update

# Hangfire dashboard (dev only)
# http://localhost:5073/hangfire

# OpenAPI spec (dev only)
# http://localhost:5073/openapi

# Manually trigger the daily news job
# GET http://localhost:5073/api/telegram/run
```

---

## API Endpoints

### Chat

| Method | Endpoint | Body / Params | Description |
| --- | --- | --- | --- |
| `POST` | `/api/chat` | `{message, sessionId?}` | Full RAG response — returns `{reply, sessionId, assistantMessageId, sources}` |
| `POST` | `/api/chat/stream` | `{message, sessionId?}` | Streaming SSE — token events then `done` event with `assistantMessageId` |
| `POST` | `/api/chat/feedback` | `{messageId, score, correction?}` | Rate assistant message: `score` = `1` or `-1`; thumbs-up promotes to golden pool |
| `GET` | `/api/chat/sessions` | — | List all sessions with title and last activity |
| `GET` | `/api/chat/history/{sessionId}` | — | All messages for a session (includes `id` and `feedbackScore`) |
| `DELETE` | `/api/chat/history/{sessionId}` | — | Soft-delete all messages in a session |
| `GET` | `/api/chat/health` | — | Quick readiness check |

### Documents

| Method | Endpoint | Description |
| --- | --- | --- |
| `POST` | `/api/documents/upload` | Upload PDF — returns `202 Accepted`, processes in background |
| `GET` | `/api/documents/{id}/status` | Poll for `Pending / Processing / Indexed / Failed` |
| `GET` | `/api/documents` | List all uploaded documents |

### System

| Method | Endpoint | Description |
| --- | --- | --- |
| `GET` | `/api/health` | Full system check: PostgreSQL + Qdrant + Ollama |
| `GET` | `/api/telegram/run` | Manually trigger daily news briefing |
| `POST` | `/api/telegram/update` | Telegram webhook endpoint |

---

## Migrations History

| Migration | Date | Change |
| --- | --- | --- |
| `InitialCreate` | 2026-06-03 | SentNews, documents, document_chunks, chat_history |
| `AddIsDeletedToChatHistory` | 2026-06-28 | Soft-delete support on chat_history |
| `AddFeedbackToHistory` | 2026-07-18 | FeedbackScore, UserCorrection, IsGolden on chat_history |

---

## Docker

```bash
# Start Qdrant
docker-compose up -d

# Ollama runs on the host (not in Docker) so it can use GPU acceleration
ollama serve
```
