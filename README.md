# TechAgent

AI Assistant Platform — Oil & Gas Intelligence + Tech News Briefing

[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com)
[![Angular](https://img.shields.io/badge/Angular-19-red)](https://angular.dev)
[![Ollama](https://img.shields.io/badge/Ollama-Local%20AI-orange)](https://ollama.ai)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)](https://postgresql.org)
[![Qdrant](https://img.shields.io/badge/Qdrant-Vector%20DB-green)](https://qdrant.tech)

> **Domain Restricted:** The Oil &amp; Gas AI chatbot answers ONLY O&G industry questions.
> Off-topic messages are rejected by a two-layer guard (keywords + semantic similarity).

---

## What TechAgent Does

TechAgent is a full-stack AI platform with an ASP.NET Core 9 backend and an Angular 19 frontend.

| Feature | What It Does |
| --- | --- |
| **Oil &amp; Gas AI Chat** | Answers O&G questions grounded in your uploaded company documents (RAG) |
| **Angular Chat UI** | Syncfusion-powered chat interface — connects to the API via session-aware HTTP |
| **Tech News Briefing** | Fetches RSS feeds daily, summarises tech news via Ollama → sends to Telegram |

Both AI features run fully **offline** using a local Ollama instance. Claude and OpenAI are optional fallbacks.

---

## Technology Stack

### Backend (TechAgent.API)

| Layer | Technology | Version | Purpose |
| --- | --- | --- | --- |
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
| GraphQL | HotChocolate | 16.0.9 | Available for future use |

### Frontend (TechAgent.Client)

| Layer | Technology | Version | Purpose |
| --- | --- | --- | --- |
| Framework | Angular | 19.1 | Standalone component architecture |
| Language | TypeScript | 5.7 | Type-safe frontend code |
| Chat UI | Syncfusion EJ2 Interactive Chat | 33.2 | Full-featured chat component |
| HTTP | Angular HttpClient | — | Session-aware API communication |
| Styles | Syncfusion Material theme | — | Chat UI styling |

---

## Project Structure

```
TechAgent/
├── README.md                         ← This file
├── .gitignore
│
├── TechAgent.API/                    ← ASP.NET Core 9 backend
│   ├── TechAgent.sln
│   ├── TechAgent.csproj
│   ├── Program.cs                    ← DI registration + middleware pipeline
│   ├── appsettings.json              ← Non-secret config (placeholders for secrets)
│   ├── docker-compose.yml            ← Qdrant container + API env vars
│   ├── .env.example                  ← Template for Docker secrets
│   ├── TechAgent.http                ← HTTP request test file
│   │
│   ├── Controllers/
│   │   ├── ChatController.cs         ← POST /api/chat (RAG-powered O&G chat)
│   │   ├── TelegramController.cs     ← POST /api/telegram/update (webhook)
│   │   ├── DocumentController.cs     ← POST /api/documents/upload (PDF indexing)
│   │   └── HealthController.cs       ← GET /api/health (system check)
│   │
│   ├── Interfaces/
│   │   ├── IAIService.cs
│   │   ├── IDocumentIngestionService.cs
│   │   ├── IDomainGuardService.cs
│   │   ├── IEmbeddingService.cs
│   │   ├── IOllamaONGService.cs
│   │   ├── IQdrantService.cs
│   │   └── IRagService.cs
│   │
│   ├── Jobs/
│   │   └── DailyJob.cs               ← Hangfire daily news briefing job
│   │
│   ├── Services/
│   │   ├── AIService.cs              ← Unified AI provider (Ollama → Claude fallback)
│   │   ├── OllamaService.cs          ← Local LLM calls (chat + news summary)
│   │   ├── RagService.cs             ← Full RAG pipeline (embed→search→load→generate)
│   │   ├── EmbeddingService.cs       ← nomic-embed-text via Ollama /api/embed
│   │   ├── QdrantService.cs          ← Vector DB operations (HNSW, gRPC)
│   │   ├── TextChunker.cs            ← Sentence-aware parent-child document splitting
│   │   ├── DomainGuardService.cs     ← Two-layer O&G domain restriction
│   │   ├── DocumentIngestionService.cs ← PDF → chunks → embed → Qdrant (background queue)
│   │   ├── TelegramService.cs        ← Telegram bot (send + webhook handler)
│   │   └── NewsService.cs            ← RSS feed reader + news preparation
│   │
│   ├── Models/
│   │   ├── RagModels.cs              ← ChatRequest/Response, Document, DocumentChunk
│   │   ├── Document.cs               ← PDF document entity
│   │   ├── DocumentChunk.cs          ← Text chunk entity
│   │   ├── SentNews.cs               ← News deduplication entity
│   │   └── AppSettings.cs            ← NewsSettings config class
│   │
│   └── Data/
│       ├── AppDbContext.cs            ← EF Core context (SentNews + Documents + Chunks + History)
│       ├── SentNews.sql               ← Manual SQL reference
│       └── Olama/
│           ├── Modelfile              ← oilgas-assistant Ollama model definition
│           └── OlamaModel.README      ← Setup notes for the custom model
│
└── TechAgent.Client/                 ← Angular 19 frontend
    ├── angular.json                  ← Build config + environment file replacements
    ├── package.json
    ├── tsconfig.json
    │
    └── src/
        ├── main.ts                   ← Bootstrap + Syncfusion license registration
        ├── styles.css                ← Global styles + Syncfusion Material theme
        ├── index.html
        │
        ├── environments/
        │   ├── environment.ts        ← Dev: apiUrl = http://localhost:5073
        │   └── environment.prod.ts   ← Prod: apiUrl = your production URL
        │
        └── app/
            ├── app.component.ts      ← Root component (standalone)
            ├── app.component.html    ← Renders <app-chat>
            ├── app.config.ts         ← provideHttpClient + zone change detection
            └── chat/
                ├── chat.component.ts ← Syncfusion ChatUI, session management
                ├── chat.component.html ← <ejs-chatui> + typing indicator
                ├── chat.component.css
                └── chat.service.ts   ← HTTP POST /api/chat, sessionId tracking
```

---

## API Endpoints

| Method | Endpoint | Purpose |
| --- | --- | --- |
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

## How the Angular Chat UI Works

```
User types in Syncfusion ChatUI
         ↓
ChatComponent.onMessageSend()
         ↓
ChatService.send(message, sessionId) → POST /api/chat
         ↓
API returns { reply, sessionId, isSuccess, wasRefused }
         ↓
sessionId stored in ChatService (persists across messages)
         ↓
chatUI.addMessage() renders AI reply
```

## How the Tech News Works (Telegram Briefing)

```
Daily at 9:00 AM (Hangfire):
         ↓
1. Fetch RSS feeds (filtered by keywords: .NET, Angular, AI, etc.)
2. Deduplicate against SentNews table in PostgreSQL
3. Summarise via Ollama (tech briefing format)
4. Fallback to Claude if Ollama fails
5. Send to Telegram channel via bot API
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

### 4. Run Migrations and Start API

```bash
cd TechAgent.API
dotnet ef migrations add InitialCreate -o Data/Migrations
dotnet ef database update
dotnet run
# API available at http://localhost:5073
```

### 5. Start Angular Frontend

```bash
cd TechAgent.Client
npm install
ng serve
# UI available at http://localhost:4200
```

### 6. Test the API

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

Non-secret settings live in `appsettings.json`. Secret keys must be supplied via user-secrets (local) or `.env` (Docker) — never hardcoded.

| Key | Purpose | Secret? |
| --- | --- | --- |
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

## Database Tables

| Table | Purpose |
| --- | --- |
| `SentNews` | Deduplication store for news items already sent to Telegram |
| `documents` | Metadata for uploaded PDF files |
| `document_chunks` | Parent text chunks (600 words) used for LLM context |
| `chat_history` | Conversation sessions (last 8 turns sent to Ollama) |

---

## Key Design Decisions

| Decision | Reason |
| --- | --- |
| Standalone Angular components | No NgModule boilerplate — each component declares its own imports |
| Syncfusion ChatUI | Production-ready chat component with typing indicators and message threading |
| Session ID pattern | API owns conversation history server-side; client only tracks the session token |
| `takeUntilDestroyed` on HTTP | Prevents callbacks firing on a destroyed component if a request is in-flight |
| Two-layer domain guard | Keyword check is fast (microseconds); semantic check catches edge cases |
| Parent-child RAG chunking | Small chunks (150w) → precise Qdrant search. Large parent (600w) → richer LLM answer |
| stream:false to Ollama | Simpler code, no SSE needed, Angular gets one clean JSON response |
| Qdrant (not pgvector) | Dedicated vector DB: better HNSW performance as document count grows |
| Background ingestion queue | Upload returns 202 instantly; large PDFs don't timeout |
| HybridCache | Identical questions answered from memory; prevents model overload |
| AIService fallback | Ollama → Claude fallback for news briefing; resilient to local model failures |

---

## Known Limitations

- PdfPig only reads **text-layer PDFs**. Scanned (image) PDFs return no text.
- Ollama on CPU is slow (15–60 seconds per answer). GPU recommended.
- nomic-embed-text is English-optimised; accuracy lower for Arabic documents.
- Qdrant.Client SDK uses gRPC (port 6334), not REST (port 6333).

---

## Documentation

| File | Contents |
| --- | --- |
| `README.md` | Project overview (this file) |
| `TechAgent.API/README.md` | API secrets setup, config reference, Docker guide |
| `TechAgent.Client/README.md` | Angular dev server, build, and test commands |
