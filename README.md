# TechAgent

AI Assistant Platform — Oil & Gas Intelligence + Tech News Briefing

[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com)
[![Angular](https://img.shields.io/badge/Angular-19-red)](https://angular.dev)
[![Ollama](https://img.shields.io/badge/Ollama-Local%20AI-orange)](https://ollama.ai)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)](https://postgresql.org)
[![Qdrant](https://img.shields.io/badge/Qdrant-Vector%20DB-green)](https://qdrant.tech)

> **Domain Restricted:** The Oil & Gas AI chatbot answers ONLY O&G industry questions.
> Off-topic messages are rejected by a two-layer guard (keywords + semantic similarity).

---

## What TechAgent Does

TechAgent is a full-stack AI platform with an ASP.NET Core 9 backend and an Angular 19 frontend.

| Feature | What It Does |
| --- | --- |
| **Oil & Gas AI Chat** | Answers O&G questions grounded in your uploaded company documents (RAG pipeline) |
| **Streaming Responses** | AI tokens stream in real-time via Server-Sent Events — no waiting for the full reply |
| **Feedback Loop** | Thumbs up/down on every AI answer; verified answers become golden few-shot examples |
| **Session Management** | Full conversation history per session — restore past chats from the sidebar |
| **Document Upload** | Upload PDF manuals; background indexing with live status polling |
| **Tech News Briefing** | Fetches RSS feeds daily, summarises via Ollama → sends to Telegram |

All AI features run fully **offline** using a local Ollama instance. Claude and OpenAI are optional fallbacks.

---

## Technology Stack

### Backend (TechAgent.API)

| Layer | Technology | Version | Purpose |
| --- | --- | --- | --- |
| Runtime | .NET / ASP.NET Core | 9.0 | Web API |
| Database | PostgreSQL | 16 | Conversation history, document chunks, feedback |
| ORM | Entity Framework Core + Npgsql | 9.0.0 | Database access (pooled DbContextFactory) |
| Vector DB | Qdrant | 1.x | Embedding search (Qdrant.Client 1.12.0, gRPC) |
| Local AI | Ollama | latest | Runs LLMs locally (GPU or CPU) |
| Chat Model | oilgas-assistant | (custom) | llama3 + Oil & Gas Modelfile (8B params) |
| Embedding | nomic-embed-text | latest | 768-dim embeddings — task-prefixed for accuracy |
| Fallback AI | Claude (Anthropic.SDK) | 5.10.0 | News summarisation fallback |
| Fallback AI | OpenAI | 2.10.0 | Optional second fallback |
| Telegram | Telegram Bot API | — | Sends daily briefings |
| Jobs | Hangfire + MemoryStorage | 1.8.23 | Daily scheduled tasks |
| Caching | HybridCache (.NET 9) | 9.0.0 | 30-min in-memory answer cache |
| PDF | PdfPig | 0.1.9 | Text extraction from uploaded PDFs |
| AI Abstraction | Microsoft.Extensions.AI | 10.6.0 | Unified IChatClient / IEmbeddingGenerator |

### Frontend (TechAgent.Client)

| Layer | Technology | Version | Purpose |
| --- | --- | --- | --- |
| Framework | Angular | 19.1 | Standalone component architecture |
| Language | TypeScript | 5.7 | Type-safe frontend code |
| Chat UI | Syncfusion EJ2 Interactive Chat | 33.2 | Chat component with message threading |
| HTTP | Angular HttpClient + Fetch API | — | REST + SSE streaming |
| Styles | Syncfusion Material theme | — | Chat UI styling |

---

## Project Structure

```
TechAgent/
├── README.md                              ← This file
├── MODEL-TRAINING-STRATEGY.md             ← Research doc: feedback loop + RAG improvement roadmap
├── .gitignore
│
├── TechAgent.API/                         ← ASP.NET Core 9 backend
│   ├── TechAgent.sln
│   ├── TechAgent.csproj
│   ├── Program.cs                         ← DI registration + AI provider selection + Hangfire
│   ├── appsettings.json                   ← Non-secret config
│   ├── appsettings.Development.json       ← DB connection (gitignored)
│   ├── docker-compose.yml                 ← Qdrant container
│   ├── .env.example                       ← Template for Docker secrets
│   │
│   ├── Controllers/
│   │   ├── ChatController.cs              ← All chat endpoints (RAG, stream, feedback, sessions)
│   │   ├── DocumentController.cs          ← PDF upload + status + list
│   │   ├── TelegramController.cs          ← Webhook + manual trigger
│   │   └── HealthController.cs            ← Full system health check
│   │
│   ├── Interfaces/
│   │   ├── IAIService.cs                  ← ChatAsync, StreamAsync, GetEmbeddingAsync
│   │   ├── IRagService.cs                 ← AskAsync, StreamAsync
│   │   ├── IQdrantService.cs
│   │   ├── IDomainGuardService.cs
│   │   ├── IEmbeddingService.cs
│   │   └── IDocumentIngestionService.cs
│   │
│   ├── Jobs/
│   │   └── DailyJob.cs                    ← Hangfire job: fetch → summarise → send to Telegram
│   │
│   ├── Services/
│   │   ├── AIService.cs                   ← Unified AI provider (Ollama → Claude → OpenAI)
│   │   ├── RagService.cs                  ← Full RAG pipeline + golden examples injection
│   │   ├── EmbeddingService.cs            ← nomic-embed-text via Ollama /api/embed
│   │   ├── QdrantService.cs               ← Vector DB (upsert, search, delete via gRPC)
│   │   ├── TextChunker.cs                 ← Sentence-aware parent(600w)/child(150w) splitting
│   │   ├── DomainGuardService.cs          ← Two-layer O&G domain restriction
│   │   ├── DocumentIngestionService.cs    ← PDF → chunk → embed → Qdrant (background queue)
│   │   ├── OllamaService.cs               ← Direct Ollama HTTP client (news summarisation)
│   │   ├── TelegramService.cs             ← Send messages + webhook handler
│   │   └── NewsService.cs                 ← RSS feed reader + deduplication
│   │
│   ├── Models/
│   │   ├── RagModels.cs                   ← All RAG DTOs: request, response, stream chunk, feedback
│   │   ├── Document.cs                    ← PDF document entity
│   │   ├── DocumentChunk.cs               ← Parent/child text chunk entity
│   │   ├── SentNews.cs                    ← News deduplication entity
│   │   └── AppSettings.cs                 ← Strongly-typed config classes
│   │
│   └── Data/
│       ├── AppDbContext.cs                ← EF Core context
│       ├── Migrations/                    ← EF Core migration history
│       └── Olama/
│           ├── Modelfile                  ← oilgas-assistant custom Ollama model
│           └── OlamaModel.README
│
└── TechAgent.Client/                      ← Angular 19 frontend
    ├── angular.json
    ├── package.json
    └── src/
        ├── main.ts                        ← Bootstrap + Syncfusion license
        ├── styles.css                     ← Global styles + Syncfusion Material theme
        └── app/
            ├── app.component.ts           ← Root layout
            ├── app.config.ts              ← provideHttpClient + routing
            ├── theme.service.ts           ← Light/dark mode toggle
            ├── chat/
            │   ├── chat.component.ts      ← Streaming chat, session tracking, feedback
            │   ├── chat.component.html    ← ejs-chatui + feedback bar + input footer
            │   ├── chat.component.css
            │   ├── chat.service.ts        ← HTTP/SSE client: stream, sessions, feedback
            │   ├── models/chat.model.ts   ← ChatHistoryEntry, SessionSummary
            │   └── constants/
            ├── documents/
            │   ├── documents.component.ts ← File picker + upload + status polling
            │   └── document.service.ts    ← HTTP client for /api/documents
            └── sidebar/
                └── sidebar.component.ts  ← Session list, new chat, delete session
```

---

## API Endpoints

### Chat

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `POST` | `/api/chat` | RAG-powered O&G chat — returns full response |
| `POST` | `/api/chat/stream` | Same RAG pipeline — streams tokens via SSE |
| `POST` | `/api/chat/feedback` | Rate an assistant message (thumbs up/down) |
| `GET` | `/api/chat/sessions` | List all conversation sessions |
| `GET` | `/api/chat/history/{sessionId}` | Restore past messages for a session |
| `DELETE` | `/api/chat/history/{sessionId}` | Soft-delete a session |
| `GET` | `/api/chat/health` | Quick AI readiness check |

### Documents

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `POST` | `/api/documents/upload` | Upload a PDF (returns 202 — processes in background) |
| `GET` | `/api/documents/{id}/status` | Poll indexing progress |
| `GET` | `/api/documents` | List all uploaded documents |

### System

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/api/health` | Full system check (DB + Qdrant + Ollama) |
| `GET` | `/api/telegram/run` | Manually trigger the daily news briefing |
| `POST` | `/api/telegram/update` | Telegram webhook |
| `GET` | `/hangfire` | Hangfire job dashboard (dev only) |

---

## How the Oil & Gas Chat Works (RAG Pipeline)

```
User asks: "What is the BOP testing interval in our HSE manual?"
         ↓
1. Embed question → 768-dim vector (nomic-embed-text, "search_query:" prefix)
2. Domain guard — Layer 1: keyword blocklist (cooking, movies, sports...)
                  Layer 2: cosine similarity to O&G centroid (10 domain phrases)
3. Golden examples — fetch up to 3 expert-verified Q&A pairs from chat_history
4. Vector search Qdrant → top-4 most similar child chunks (150 words each)
5. Load parent chunks → 600-word sections from PostgreSQL (rich LLM context)
6. Build prompt:
     [VERIFIED EXAMPLES — expert answers from feedback]
     [CONTEXT START — document sections]
     [CONVERSATION HISTORY — last 8 turns]
     QUESTION: ...
     ANSWER:
7. Call oilgas-assistant via Ollama (stream or full)
8. Cache answer by SHA-256(question) — HybridCache, 30-min TTL (first turn only)
9. Persist Q&A to chat_history; return assistantMessageId for feedback
10. Return: { reply, sources, sessionId, wasRefused, assistantMessageId }
```

## How the Feedback Loop Works

```
User rates answer 👍
         ↓
POST /api/chat/feedback { messageId: 66, score: 1 }
         ↓
chat_history row → IsGolden = true, FeedbackScore = 1
         ↓
Next question triggers RagService.AskAsync
         ↓
Fetches golden pairs (WHERE IsGolden = true ORDER BY FeedbackScore DESC LIMIT 3)
         ↓
Injected as few-shot examples at top of LLM prompt
         ↓
Model produces higher-quality answers shaped by real expert-verified examples
```

With a correction:

```
User rates 👍 + provides correction text
         ↓
POST /api/chat/feedback { messageId: 66, score: 1, correction: "Actually it's 14 days per API RP 53" }
         ↓
New ChatSessionHistory row saved with correction text and IsGolden = true
         ↓
Correction flows into future prompts as a golden example
```

## How Streaming Works

```
POST /api/chat/stream  (Content-Type: application/json)
         ↓
Server-Sent Events:
  data: {"type":"token","value":"Reservoir "}
  data: {"type":"token","value":"porosity "}
  data: {"type":"token","value":"refers to..."}
  data: {"type":"done","sessionId":"...","assistantMessageId":66}
         ↓
Angular reads stream via Fetch ReadableStream
Tokens appended to the AI message bubble in real-time
On "done": sessionId saved to localStorage; feedback bar appears
```

## How the Tech News Works (Telegram Briefing)

```
Daily at 9:00 AM UTC (Hangfire):
         ↓
1. Fetch RSS feeds → filter by keywords (.NET, Angular, AI, cloud, etc.)
2. Deduplicate against SentNews table (unique index on Title)
3. Cap at 5 items per run
4. Summarise via Ollama in Telegram-friendly format
5. Fallback to Claude if Ollama unreachable
6. Send to Telegram channel via Bot API
```

---

## Quick Start

### Prerequisites

- Docker Desktop
- .NET 9 SDK
- Node.js 20+
- [Ollama](https://ollama.ai/download)
- `dotnet tool install --global dotnet-ef`

### 1. Start Qdrant

```bash
cd TechAgent.API
docker compose up -d
```

### 2. Set Up Ollama Models

```bash
ollama pull llama3
ollama pull nomic-embed-text
ollama create oilgas-assistant -f Data/Olama/Modelfile
ollama serve
```

### 3. Configure Secrets

Secrets are never stored in `appsettings.json`. See **[TechAgent.API/README.md](TechAgent.API/README.md)** for the full setup — local dev uses `dotnet user-secrets`, Docker uses a `.env` file.

Minimum required secrets for local dev:

```bash
cd TechAgent.API
dotnet user-secrets set "Telegram:BotToken" "your-bot-token"
dotnet user-secrets set "Telegram:ChatId"   "your-chat-id"
```

Set the DB connection in `appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=techagent;Username=appUser;Password=appUser@123"
  }
}
```

### 4. Run Migrations and Start API

```bash
cd TechAgent.API
dotnet ef migrations add InitialCreate -o Data/Migrations   # first time only
dotnet ef database update
dotnet run
# API: http://localhost:5073
```

### 5. Start Angular Frontend

```bash
cd TechAgent.Client
npm install
npm start
# UI: http://localhost:4200
```

### 6. Test the API

```bash
# Health check
curl http://localhost:5073/api/health

# O&G chat (full response)
curl -X POST http://localhost:5073/api/chat \
     -H "Content-Type: application/json" \
     -d '{"message":"What is a blowout preventer?"}'

# Rate the answer (use assistantMessageId from the response)
curl -X POST http://localhost:5073/api/chat/feedback \
     -H "Content-Type: application/json" \
     -d '{"messageId":1,"score":1}'

# Upload a document
curl -X POST http://localhost:5073/api/documents/upload \
     -F "file=@your_hse_manual.pdf"
```

---

## Database Tables

| Table | Key Columns | Purpose |
| --- | --- | --- |
| `SentNews` | Id, Title (unique), Link, Hash, SentDate | News deduplication for Telegram briefings |
| `documents` | Id, FileName, Status, ChunkCount, IndexedAt | Uploaded PDF metadata |
| `document_chunks` | Id, DocumentId, ChildText (150w), ParentText (600w), ChunkIndex | RAG text chunks — child embedded in Qdrant, parent stored here |
| `chat_history` | Id, SessionId, Role, Message, WasRefused, IsDeleted, **FeedbackScore, UserCorrection, IsGolden**, CreatedAt | Conversation turns + feedback ratings + golden example pool |

### chat_history feedback columns

| Column | Type | Purpose |
| --- | --- | --- |
| `FeedbackScore` | `int?` | `1` = thumbs up, `-1` = thumbs down, `null` = unrated |
| `UserCorrection` | `string?` | Corrected text provided by the user when an answer was wrong |
| `IsGolden` | `bool` | `true` = this answer is injected as a few-shot example in future prompts |

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
| `Qdrant:Collection` | Vector collection name (default `oilgas_documents`) | No |
| `AppSettings:NewsSettings:Feeds` | RSS feed URLs | No |
| `AppSettings:NewsSettings:Keywords` | Title-filter keywords for news | No |

---

## Key Design Decisions

| Decision | Reason |
| --- | --- |
| **Feedback loop → few-shot golden examples** | Verified answers injected as context; the model improves from real usage without retraining |
| **`assistantMessageId` in every response** | Client references the DB row ID to submit ratings; preserves the feedback link across sessions |
| **Streaming via SSE (`/api/chat/stream`)** | Tokens visible immediately; no timeout risk on long responses; `done` event carries `assistantMessageId` |
| **Soft-delete on chat_history** | `IsDeleted = true` hides sessions from UI while preserving golden examples in the feedback pool |
| **Parent-child RAG chunking** | Child chunks (150w) → precise Qdrant similarity search. Parent chunks (600w) → richer LLM context without bloating the vector index |
| **`search_query:` / `search_document:` prefixes** | nomic-embed-text is a task-prefixed model; using the right prefix measurably improves retrieval recall |
| **HybridCache (30 min, SHA-256 key)** | Stampede-safe: identical questions answered instantly; cache skipped for multi-turn conversations |
| **Two-layer domain guard** | Keyword check is microseconds (fast reject). Cosine similarity to O&G centroid catches semantic edge cases the keyword list misses |
| **Qdrant over pgvector** | Dedicated HNSW index; better performance as document count grows; cleaner separation of concerns |
| **Background ingestion queue (Channel of T)** | Upload returns 202 instantly; large PDFs processed without HTTP timeout |
| **Session-owned history** | API owns conversation state server-side; client only carries the session token in localStorage |
| **Standalone Angular components** | No NgModule boilerplate; each component explicitly declares its imports |

---

## Known Limitations

- PdfPig only reads **text-layer PDFs**. Scanned / image-based PDFs return no text.
- Ollama on CPU is slow (15–60 s per response). GPU strongly recommended for production.
- nomic-embed-text is English-optimised; accuracy is lower for Arabic or mixed-language documents.
- Qdrant.Client SDK uses gRPC (port 6334), not REST (port 6333).
- Hangfire uses in-memory storage — recurring jobs re-register on every startup and do not persist across restarts.

---

## Model Improvement Roadmap

See **[MODEL-TRAINING-STRATEGY.md](MODEL-TRAINING-STRATEGY.md)** for the full research document covering:

- Feedback loop + golden few-shot examples (implemented)
- Continuous automated RAG knowledge ingestion
- Ollama Modelfile rebuilds from golden data
- HyDE (Hypothetical Document Embeddings) for better retrieval
- Embedding model upgrade (`mxbai-embed-large`)
- Cross-encoder reranking

---

## Documentation

| File | Contents |
| --- | --- |
| `README.md` | Project overview, architecture, quick start (this file) |
| `TechAgent.API/README.md` | API secrets setup, config reference, Docker guide |
| `MODEL-TRAINING-STRATEGY.md` | Research: continuous model improvement strategy + implementation roadmap |
