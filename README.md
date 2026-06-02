# TechAgent
### AI Assistant Platform — Oil &amp; Gas Intelligence + Tech News Briefing

[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com)
[![Ollama](https://img.shields.io/badge/Ollama-Local%20AI-orange)](https://ollama.ai)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)](https://postgresql.org)
[![Qdrant](https://img.shields.io/badge/Qdrant-Vector%20DB-green)](https://qdrant.tech)

> **Domain Restricted:** The Oil &amp; Gas AI chatbot answers ONLY O&G industry questions.
> Off-topic messages are rejected by a two-layer guard (keywords + semantic similarity).

---

## What TechAgent Does

TechAgent is a single ASP.NET Core 9 backend that runs two independent AI features:

| Feature | What It Does |
|---------|-------------|
| **Oil &amp; Gas AI Chat** | Answers O&G questions grounded in your uploaded company documents (RAG) |
| **Tech News Briefing** | Fetches RSS feeds daily, summarises tech news via Ollama → sends to Telegram |

Both run fully **offline** using a local Ollama instance. Claude and OpenAI are optional fallbacks.

---

## Technology Stack

| Layer | Technology | Version | Purpose |
|-------|-----------|---------|---------|
| Runtime | .NET / ASP.NET Core | 9.0 | Web API |
| Database | PostgreSQL | 16 | Structured data + document chunks |
| ORM | Entity Framework Core + Npgsql | 9.0.0 | Database access |
| Vector DB | Qdrant | 1.x | Embedding search (via Qdrant.Client 1.12.0) |
| Local AI | Ollama | latest | Runs LLMs locally |
| Chat Model | oilgas-assistant | (custom) | llama3 + O&G Modelfile |
| Embedding | nomic-embed-text | latest | 768-dim text embeddings for RAG |
| Fallback AI | Claude (Anthropic.SDK) | 5.10.0 | News summarisation fallback |
| Fallback AI | OpenAI | 2.10.0 | Optional fallback |
| Telegram | Telegram Bot API | — | Sends daily briefings |
| Jobs | Hangfire + MemoryStorage | 1.8.23 | Daily scheduled tasks |
| Caching | HybridCache (.NET 9) | 9.0.0 | In-memory answer caching |
| PDF | PdfPig | 0.1.9 | Extracts text from uploaded PDFs |
| GraphQL | HotChocolate | 16.0.9 | (installed, available for future use) |

---

## Project Structure

```
TechAgent/
├── TechAgent.sln
├── TechAgent.csproj              ← NuGet packages
├── TechAgent.http                ← HTTP request test file
├── Modelfile                     ← Ollama oilgas-assistant configuration
├── Program.cs                    ← DI registration + pipeline
├── appsettings.json              ← All configuration (copy, fill secrets)
├── docker-compose.yml            ← Qdrant container
├── README.md                     ← This file
├── GUIDE-PLAIN-ENGLISH.md        ← Plain-English explanation of every AI concept
├── SETUP-GUIDE.md                ← Step-by-step setup instructions
│
├── Controllers/
│   ├── ChatController.cs         ← POST /api/chat (RAG-powered O&G chat)
│   ├── TelegramController.cs     ← POST /api/telegram/update (webhook)
│   ├── DocumentController.cs     ← POST /api/documents/upload (PDF indexing)
│   └── HealthController.cs       ← GET /api/health (system check)
│
├── Services/
│   ├── OlamaONGService.cs        ← Oil & Gas direct chat (domain guard + Ollama)
│   ├── RagService.cs             ← Full RAG pipeline (embed→search→load→generate)
│   ├── EmbeddingService.cs       ← nomic-embed-text via Ollama /api/embed
│   ├── QdrantService.cs          ← Vector DB operations (HNSW, gRPC)
│   ├── TextChunker.cs            ← Sentence-aware parent-child document splitting
│   ├── DomainGuardService.cs     ← Two-layer O&G domain restriction
│   ├── DocumentIngestionService.cs ← PDF → chunks → embed → Qdrant + background queue
│   ├── OllamaService.cs          ← Tech news summariser (Malik's briefing)
│   ├── ClaudeService.cs          ← Claude AI news summariser (fallback)
│   ├── SmartAIService.cs         ← Ollama → Claude fallback pattern
│   ├── TelegramService.cs        ← Telegram bot (send + webhook handler)
│   ├── NewsService.cs            ← RSS feed reader + news preparation
│   └── OpenAIService.cs          ← OpenAI summariser (optional)
│
├── Models/
│   ├── ChatModels.cs             ← ChatRequest, ChatResponse, OllamaMessage (existing)
│   ├── RagModels.cs              ← RagChatRequest/Response, Document, DocumentChunk
│   └── AppSettings.cs            ← NewsSettings, AppSettings config classes
│
└── Data/
    ├── AppDbContext.cs            ← EF Core context (SentNews + Document + Chunk + History)
    ├── SentNews.cs               ← News deduplication entity
    └── SentNews.sql              ← Manual SQL for SentNews table (reference)
```

---

## API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `POST` | `/api/chat` | RAG-powered Oil &amp; Gas chat |
| `POST` | `/api/chat/direct` | Direct O&G chat (no RAG context) |
| `GET` | `/api/chat/health` | Quick readiness |
| `POST` | `/api/documents/upload` | Upload PDF company document |
| `GET` | `/api/documents/{id}/status` | Check indexing progress |
| `GET` | `/api/documents` | List all documents |
| `GET` | `/api/health` | Full system health (DB + Qdrant + Ollama) |
| `GET` | `/api/telegram/run` | Manually trigger daily news job |
| `POST` | `/api/telegram/update` | Telegram webhook |
| `GET` | `/hangfire` | Hangfire job dashboard |

---

## How the Oil &amp; Gas Chat Works (RAG Pipeline)

```
User asks: "What is the BOP testing interval in our HSE manual?"
         ↓
1. Embed question → 768-number fingerprint (nomic-embed-text, offline)
2. Domain guard → Layer 1: keywords. Layer 2: cosine similarity to O&G centroid
3. Search Qdrant → Top-4 most relevant document sections
4. Load parent chunks → 600-word sections from PostgreSQL (rich context)
5. Build prompt → [document context] + [last 8 conversation turns] + [question]
6. Call Ollama oilgas-assistant (stream:false — full response at once)
7. Cache answer by SHA-256 hash (HybridCache, 30-min TTL)
8. Return: { reply, sources, sessionId, wasRefused }
```

## How the Tech News Works (Telegram Briefing)

```
Daily at 9:00 AM (Hangfire):
         ↓
1. Fetch RSS feeds (filtered by keywords: .NET, Angular, AI, etc.)
2. Deduplicate against SentNews table in PostgreSQL
3. Summarise via Ollama (tech briefing format for "Malik Ahmed")
4. Fallback to Claude if Ollama fails
5. Send to Telegram channel via bot API
```

---

## Quick Start

### Prerequisites
- Docker Desktop
- .NET 9 SDK
- Ollama: https://ollama.ai/download
- `dotnet tool install --global dotnet-ef`

### 1. Start Qdrant
```bash
docker compose up -d
```

### 2. Set Up Ollama Models
```bash
ollama pull llama3
ollama pull nomic-embed-text
ollama create oilgas-assistant -f Modelfile
ollama serve
```

### 3. Configure
```bash
# Copy appsettings.json and fill in your secrets
# Minimum required:
#   ConnectionStrings:DefaultConnection  ← your PostgreSQL
#   Telegram:BotToken and ChatId         ← for news briefing
#   Claude:ApiKey                        ← optional fallback
```

### 4. Run Migrations and Start
```bash
dotnet ef migrations add InitialCreate -o Data/Migrations
dotnet ef database update
dotnet run
```

### 5. Test
```bash
# Health check
curl http://localhost:5073/api/health

# O&G chat
curl -X POST http://localhost:5073/api/chat \
     -H "Content-Type: application/json" \
     -d '{"message":"What is a blowout preventer?"}'

# Upload document
curl -X POST http://localhost:5073/api/documents/upload \
     -F "file=@your_hse_manual.pdf"
```

---

## Configuration Reference

All settings live in `appsettings.json`:

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Ollama:BaseUrl` | Ollama server (default `http://localhost:11434`) |
| `Ollama:ONGModelName` | O&G chat model (default `oilgas-assistant`) |
| `Ollama:EmbeddingModel` | Embedding model (must be `nomic-embed-text`) |
| `Qdrant:Host` / `Qdrant:Port` | Qdrant gRPC connection (default `localhost:6334`) |
| `Qdrant:Collection` | Collection name (default `oilgas_documents`) |
| `Telegram:BotToken` | Telegram bot API token |
| `Telegram:ChatId` | Channel/chat to send briefings to |
| `Claude:ApiKey` | Anthropic API key (fallback AI) |
| `AppSettings:NewsSettings:Feeds` | RSS feed URLs |
| `AppSettings:NewsSettings:Keywords` | Filter keywords for news |

---

## Database Tables

| Table | Purpose |
|-------|---------|
| `SentNews` | Deduplication store for news items already sent to Telegram |
| `documents` | Metadata for uploaded PDF files |
| `document_chunks` | Parent text chunks (600 words) used for LLM context |
| `chat_history` | Conversation sessions (last 8 turns sent to Ollama) |

---

## Key Design Decisions

| Decision | Reason |
|----------|--------|
| Two-layer domain guard | Keyword check is fast (microseconds); semantic check catches edge cases. Together: robust. |
| Parent-child RAG chunking | Small chunks (150w) → precise Qdrant search. Large parent (600w) → richer LLM answer. |
| stream:false to Ollama | Simpler code, no SSE needed, Angular gets one clean JSON response |
| Qdrant (not pgvector) | Dedicated vector DB: better HNSW performance as document count grows |
| Background ingestion queue | Upload returns 202 instantly; large PDFs don't timeout |
| HybridCache | Identical questions answered from memory; prevents model overload |
| SmartAIService fallback | Ollama → Claude fallback for news briefing; resilient to local model failures |

---

## Known Limitations

- PdfPig only reads **text-layer PDFs**. Scanned (image) PDFs return no text.
- Ollama on CPU is slow (15–60 seconds per answer). GPU recommended.
- nomic-embed-text is English-optimised; accuracy lower for Arabic documents.
- Qdrant.Client SDK uses gRPC (port 6334), not REST (port 6333).

---

## Documentation

| File | Contents |
|------|----------|
| `README.md` | Project overview (this file) |
| `GUIDE-PLAIN-ENGLISH.md` | Every AI concept explained simply — what is RAG, embeddings, vectors, etc. |
| `SETUP-GUIDE.md` | Full step-by-step setup with exact commands |
