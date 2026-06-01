# TechAgent

# RigMind — Complete Oil & Gas AI Solution
## .NET 9 + Angular 19 + PostgreSQL + Qdrant + GraphQL + RAG

---

## Why This Solution Is Better Than The Previous One

### Improvement 1: Small-to-Big (Parent-Child) Chunking
**Previous approach:** Equal 500-word chunks, same chunk searched AND injected into LLM.
**This approach:** 150-word child chunks in Qdrant for PRECISE search. 600-word parent chunks in
PostgreSQL for RICH context injection. When a child chunk is retrieved, we load its parent chunk
and give that to the LLM. This solves the precision-vs-context tradeoff — the model gets more
surrounding information without polluting vector search with noise.
**Verified by:** Llamaindex research + production RAG teams at Anthropic, Cohere, and Weaviate.

### Improvement 2: Semantic Domain Guard (Layer 2)
**Previous approach:** Only keyword matching (brittle, misses paraphrases, blocks legitimate edge cases).
**This approach:** TWO LAYERS:
  - Layer 1: Fast keyword check (microseconds, blocks obvious off-topic).
  - Layer 2: Cosine similarity between query embedding and a pre-computed Oil & Gas centroid vector.
    The centroid is built by averaging embeddings of 10 canonical O&G phrases.
    CRUCIALLY: This REUSES the query embedding already generated for RAG — ZERO extra Ollama call.
**Why better:** Catches "what's the optimal pump speed for artificial lift?" even without O&G keywords.
  Also avoids blocking legitimate technical questions that don't use exact keywords.

### Improvement 3: Background Processing Queue for Document Ingestion
**Previous approach:** Upload blocked until PDF was fully processed (could take minutes for large docs).
**This approach:** System.Threading.Channels.Channel<T> queue. Upload returns 202 Accepted INSTANTLY.
Angular polls GET /api/documents/{id}/status every 2 seconds until indexed or failed.
**Why better:** Better UX, avoids HTTP timeouts on large PDFs, proper separation of concerns.

### Improvement 4: Conversation History Window (Last 8 turns)
**Previous approach:** Send ALL history (unbounded — breaks on long conversations).
**This approach:** PostgreSQL-persisted history, but only last 8 messages sent to Ollama.
**Why:** Ollama model has 8192-token context window. Unlimited history causes context overflow,
degrading answer quality. 8 turns ≈ 4 user + 4 assistant ≈ ~1200 tokens, leaving ~7000 for
system prompt + RAG context + answer.

### Improvement 5: HybridCache (.NET 9 GA) for Identical Queries
**Previous approach:** No caching on chat responses.
**This approach:** SHA-256 hash of (prompt + context) as cache key. Identical questions with identical
context return instantly without hitting Ollama. HybridCache provides stampede protection — if 10 users
ask the same question simultaneously, only one calls Ollama; others wait and get the same result.

### Improvement 6: Source-Generated DataLoaders (HotChocolate 14)
**Previous approach:** Would require manually writing DataLoader classes.
**This approach:** [DataLoader] attribute + HotChocolate.Types.Analyzers generates ALL DataLoader code
at compile time. The [DataLoaderModule] assembly attribute auto-registers everything in DI.
Eliminates N+1: 50 wells with production records = 2 SQL queries, not 51.

---

## Quick Start Commands

```bash
# 1. Start infrastructure
docker compose up -d

# 2. Build Ollama model
ollama pull llama3
ollama pull nomic-embed-text
ollama create oilgas-assistant -f Modelfile
ollama serve

# 3. Backend
cd backend
dotnet restore
dotnet ef migrations add InitialCreate -o Data/Migrations
dotnet ef database update
dotnet run

# 4. Frontend
cd frontend
npm install
ng serve
# Open http://localhost:4200
```

---

## File Structure

```
OilGasAI/
├── docker-compose.yml          # PostgreSQL 16 + Qdrant
├── Modelfile                   # Ollama oilgas-assistant (Layer 1 domain guard)
│
├── backend/
│   ├── OilGasAI.API.csproj     # NuGet packages (.NET 9)
│   ├── Program.cs              # DI registration + pipeline
│   ├── appsettings.json        # All configuration
│   │
│   ├── Domain/Entities/
│   │   └── Entities.cs         # Well, ProductionRecord, HSEIncident, WellLog,
│   │                             ChatHistory, Document, DocumentChunk
│   ├── Data/
│   │   └── AppDbContext.cs     # EF Core 9 + all entity configurations
│   ├── Models/
│   │   └── Dtos.cs             # Request/Response DTOs
│   │
│   ├── Services/
│   │   ├── EmbeddingService.cs       # nomic-embed-text via Ollama /api/embed
│   │   ├── OllamaService.cs          # oilgas-assistant via /api/generate (stream:false)
│   │   ├── QdrantService.cs          # Vector DB (gRPC, HNSW m=16 ef=100)
│   │   ├── TextChunker.cs            # Parent-child chunking strategy
│   │   ├── DomainGuardService.cs     # Layer 2: keyword + semantic centroid guard
│   │   ├── DocumentIngestionService.cs # PDF → chunks → embed → Qdrant + background queue
│   │   └── RAGService.cs             # Core RAG pipeline (embed → search → parent load → generate)
│   │
│   ├── GraphQL/
│   │   ├── GraphQL.cs          # Query, Mutation, DataLoaders, WellType
│   │   └── Types/
│   │
│   └── Controllers/
│       └── Controllers.cs      # ChatController, DocumentController, HealthController
│
└── frontend/
    └── src/app/
        ├── app.config.ts           # Apollo + HttpClient + interceptors
        ├── app.component.ts        # Shell with sidebar nav
        ├── app.routes.ts           # Lazy-loaded routes
        ├── services/
        │   ├── chat.service.ts     # REST → POST /api/chat
        │   ├── well.service.ts     # GraphQL → Apollo queries
        │   ├── document.service.ts # REST → upload + poll status
        │   └── loading.service.ts  # Signal-based global loading state
        ├── interceptors/
        │   └── loading.interceptor.ts  # Functional HTTP interceptor
        └── components/
            ├── chat/               # AI chat interface (industrial dark theme)
            ├── document-upload/    # PDF upload with drag-and-drop + status polling
            └── well-dashboard/     # Wells table + KPIs + detail panel (GraphQL)
```

---

## Architecture Decisions Verified

### Why GraphQL for well data, REST for chat?
- Well data is RELATIONAL (wells → production → logs → incidents) → GraphQL's nested queries
  and DataLoaders are perfect. Angular gets exactly what it needs in one request.
- Chat is a simple request/response with binary in/out → REST is cleaner, easier to debug,
  no GraphQL schema complexity needed for a single operation.
- Document upload is multipart → REST (GraphQL handles multipart poorly).

### Why Qdrant over pgvector?
- Qdrant is a DEDICATED vector database with HNSW indexing, payload filtering, and a
  purpose-built gRPC API. For large document collections it outperforms pgvector significantly.
- pgvector is great for small collections (<100k vectors) where PostgreSQL is already the primary DB.
- Since we already have PostgreSQL for structured data, adding Qdrant for vectors gives us
  the best of both worlds without overloading PostgreSQL with vector operations.

### Why nomic-embed-text?
- 768-dim vectors (good balance of quality vs memory vs speed)
- Open source, fully local via Ollama
- Task-aware: search_query: and search_document: prefixes improve retrieval quality
- 8192-token context window (handles long document chunks)
- Specifically trained for retrieval tasks (not just generation)

### Why HotChocolate 14 over Strawberry Shake or other GraphQL libs?
- HotChocolate is the most mature, feature-complete GraphQL server for .NET
- HC14's source-generated DataLoaders (compile-time, zero runtime reflection)
- Native EF Core integration via RegisterDbContextFactory
- [UsePaging], [UseFiltering], [UseSorting], [UseProjection] out of the box

---

## Performance Numbers (Expected on Local Hardware)

| Operation                        | Expected Latency |
|----------------------------------|-----------------|
| Embedding (nomic-embed-text)     | 50-200ms         |
| Qdrant vector search (top-4)     | 5-20ms           |
| PostgreSQL parent chunk load     | 2-10ms           |
| Ollama generate (llama3, GPU)    | 2-8s             |
| Ollama generate (llama3, CPU)    | 15-60s           |
| GraphQL well query (50 wells)    | 20-80ms          |
| HybridCache hit                  | <1ms             |

---

## Important Notes

1. **Qdrant collection MUST be created with Size=768** — nomic-embed-text outputs 768-dim vectors.
   Mismatch (e.g. 1536) causes runtime errors. The QdrantService.EnsureCollectionAsync() handles this.

2. **nomic-embed-text prefix requirement** — always use "search_document:" when embedding chunks,
   and "search_query:" when embedding user queries. Omitting these degrades recall by ~15-20%.

3. **PdfPig limitation** — only works on text-based PDFs, NOT scanned/image PDFs.
   For scanned PDFs, add Tesseract OCR as a pre-processing step.

4. **HotChocolate DataLoaders** — DO NOT call .AddDataLoader<T>() in Program.cs.
   The [DataLoaderModule] + [DataLoader] attributes auto-register everything.

5. **Connection string** — the PostgreSQL connection string uses `Enlist=false` to skip
   ambient transaction checks, improving performance in high-throughput scenarios.

6. **stream:false** in Ollama — confirmed: when stream is false, Ollama buffers the full response
   and returns a single JSON object. No SSE, no chunked transfer. Angular HttpClient handles this
   as a normal HTTP response.
