# TechAgent.API

ASP.NET Core 9 backend — Oil & Gas AI chat + daily tech news briefing via Telegram.

---

## Secrets Management

Secrets are **never stored in `appsettings.json`**. The approach differs between local development and Docker.

### Local Development (`dotnet run`)

Use .NET User Secrets. They live only on your machine (`~/.microsoft/usersecrets/`) and are never committed.

> **`UserSecretsId` in `.csproj` is safe to commit.** It is a random GUID automatically generated
> by `dotnet user-secrets init` that tells .NET where to find your secrets on disk. It contains
> no secrets — without the `secrets.json` file on your machine the GUID is meaningless to anyone else.

```bash
cd TechAgent.API
# UserSecretsId is already initialised in TechAgent.csproj — skip this if already done
dotnet user-secrets init

dotnet user-secrets set "Telegram:BotToken"   "your-bot-token"
dotnet user-secrets set "Telegram:ChatId"     "your-chat-id"
dotnet user-secrets set "OpenAI:ApiKey"       "your-openai-key"
dotnet user-secrets set "Claude:ApiKey"       "your-claude-key"

# Verify
dotnet user-secrets list
```

.NET automatically merges user secrets with `appsettings.json` at runtime — no code changes needed.

Set the database connection in `appsettings.Development.json` (already gitignored):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=techagent;Username=appUser;Password=appUser@123"
  }
}
```

### Docker (`docker-compose up`)

`dotnet user-secrets` only works locally — Docker containers cannot access your host's secrets folder. Use a `.env` file instead.

```bash
cp .env.example .env
# Edit .env and fill in your real values — it is gitignored
```

`.env` file:

```env
TELEGRAM_BOT_TOKEN=your-real-token
TELEGRAM_CHAT_ID=your-real-chat-id
OPENAI_API_KEY=your-real-openai-key
CLAUDE_API_KEY=your-real-claude-key
```

`docker-compose.yml` reads these and injects them as container environment variables. ASP.NET Core maps `__` → `:` automatically (`Telegram__BotToken` → `Telegram:BotToken`), so the same config keys work transparently in both environments.

### Summary

| Environment | Secret source |
| --- | --- |
| `dotnet run` (local) | `dotnet user-secrets` → `~/.microsoft/usersecrets/` |
| `docker-compose up` | `.env` → docker-compose env vars → container |

---

## Configuration Reference

| Key | Purpose | Secret? |
| --- | ------- | ------- |
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string | Yes |
| `Telegram:BotToken` | Telegram bot API token | Yes |
| `Telegram:ChatId` | Channel/chat to send briefings to | Yes |
| `OpenAI:ApiKey` | OpenAI API key (optional fallback) | Yes |
| `Claude:ApiKey` | Anthropic API key (fallback AI) | Yes |
| `Ollama:BaseUrl` | Ollama server (default `http://localhost:11434`) | No |
| `Ollama:ONGModelName` | O&G chat model (default `oilgas-assistant`) | No |
| `Ollama:EmbeddingModel` | Embedding model (must be `nomic-embed-text`) | No |
| `Qdrant:Host` / `Qdrant:Port` | Qdrant gRPC connection (default `localhost:6334`) | No |
| `Qdrant:Collection` | Collection name (default `oilgas_documents`) | No |
| `AppSettings:NewsSettings:Feeds` | RSS feed URLs | No |
| `AppSettings:NewsSettings:Keywords` | Filter keywords for news | No |

---

## Commands

```bash
# Run
dotnet run

# Build
dotnet build

# EF Core migrations
dotnet ef migrations add <MigrationName>
dotnet ef database update

# Hangfire dashboard (dev only)
# http://localhost:5073/hangfire

# Manually trigger the daily news job
GET /api/telegram/run
```

---

## Docker

```bash
# Start Qdrant (and API if you add a service entry)
docker-compose up -d
```

Ollama runs on the **host** (not in Docker) so it can access GPU acceleration:

```bash
ollama serve
```
