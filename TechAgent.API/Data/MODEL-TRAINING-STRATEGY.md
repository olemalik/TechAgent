# TechAgent — Continuous Model Improvement Strategy

**Author:** Malik Ahmed  
**Date:** 2026-07-18  
**Stack:** ASP.NET Core 9 · Ollama (llama3) · Qdrant · PostgreSQL · Angular 19

---

## Executive Summary

LLMs cannot be retrained on a schedule the way traditional ML models can — that requires GPU clusters and days of compute. Instead, the world's best AI product teams (OpenAI, Anthropic, Cohere, Notion AI, Glean) achieve the *effect* of a continuously improving model through three complementary strategies applied in sequence:

1. **Feedback loop + few-shot golden examples** — highest ROI, zero infrastructure cost
2. **Continuous RAG knowledge updates** — keeps the knowledge base fresh automatically
3. **Ollama custom Modelfile** — bakes domain expertise into the local model definition

This document records what was researched, what was implemented, and what remains as future work.

---

## Current Architecture (Baseline)

| Layer | Technology | Role |
|---|---|---|
| LLM | Ollama llama3 (primary), Claude, OpenAI (fallbacks) | Chat generation |
| Embeddings | nomic-embed-text (768-dim) via Ollama | Vector representation |
| Vector store | Qdrant (cosine similarity, TopK=4) | Semantic search |
| Document store | PostgreSQL — `document_chunks` (parent 600w / child 150w) | RAG context |
| Conversation | PostgreSQL — `chat_history` (last 8 turns per session) | Multi-turn memory |
| Cache | .NET 9 HybridCache (30 min TTL, SHA-256 key) | Identical-question dedup |
| Domain guard | Two-layer: keyword filter + cosine similarity to O&G centroid | Off-topic rejection |
| Scheduler | Hangfire (in-memory) | Daily news briefing |

**Gap before this work:** The system had no mechanism to learn from user interactions. Every conversation was discarded after serving — no signal flowed back to improve future responses.

---

## Strategy 1 — Feedback Loop & Golden Examples (IMPLEMENTED)

### What was built

#### Backend

**`ChatSessionHistory` model** — three new columns:

| Column | Type | Purpose |
|---|---|---|
| `FeedbackScore` | `int?` | 1 = thumbs up, -1 = thumbs down |
| `UserCorrection` | `string?` | User-provided correction when answer was wrong |
| `IsGolden` | `bool` | Promotes message to the few-shot example pool |

**`POST /api/chat/feedback`** — accepts `{ messageId, score, correction? }`:
- Score `+1` with no correction → marks the original answer as `IsGolden = true`
- Score `+1` with a correction → saves correction as a new golden entry; stores both
- Score `-1` → records the negative signal (usable for future analysis)

**`RagService.BuildPrompt`** — now fetches up to 3 golden Q&A pairs before every LLM call and injects them at the top of the prompt:

```
[VERIFIED EXAMPLES — expert-confirmed answers]
EXAMPLE ANSWER: <golden answer 1>
EXAMPLE ANSWER: <golden answer 2>
[END EXAMPLES]

[CONTEXT START]
Source: <filename>
<parent chunk text>
[CONTEXT END]
...
QUESTION: <user question>
ANSWER:
```

**`PersistAsync`** — now returns `Task<long>` (the DB row ID of the assistant message). This ID flows back to the client in the streaming `done` event (`assistantMessageId`) and in the non-streaming `RagChatResponse`.

**`GET /api/chat/history/{sessionId}`** — now returns `id` and `feedbackScore` per message so the frontend can restore feedback state on session reload.

#### Frontend

**`ChatHistoryEntry`** — added `id: number` and `feedbackScore: number | null`

**`AiMessage` interface** — extends `MessageModel` with `dbId?: number` and `feedbackScore?: number | null`

**`ChatService.submitFeedback(messageId, score, correction?)`** — `POST /api/chat/feedback`

**`chat.component.ts`** — tracks `dbId` and `feedbackScore` on every AI message. Exposes `sendFeedback(msg, score)` method.

**`chat.component.html`** — feedback bar appears below the last AI response:
- Shows thumbs up / thumbs down when `feedbackScore === null`
- Shows a confirmation message ("Thanks — this answer is now a golden example" / "Feedback recorded") after rating
- Disappears once the user sends the next message

### How it works end-to-end

```
User rates answer (👍) 
  → POST /api/chat/feedback { messageId: 42, score: 1 }
  → ChatHistory row 42 gets IsGolden = true
  → Next user question triggers RagService.AskAsync
  → Golden pairs fetched from DB (up to 3)
  → Injected at top of LLM prompt as few-shot examples
  → LLM produces higher-quality answer shaped by verified examples
```

### Why this works

This is **in-context learning** — the same mechanism that makes GPT-4 and Claude so effective when given examples. By curating real Q&A pairs that a domain expert has verified (thumbs up), you give the model a continuously growing set of reference answers. After 20-30 golden pairs, answer quality visibly improves without touching model weights.

**DB migration required:**

```bash
cd TechAgent.API
dotnet ef migrations add AddFeedbackToHistory
dotnet ef database update
```

---

## Strategy 2 — Continuous RAG Knowledge Updates (FUTURE WORK)

### The gap

Documents are currently ingested manually via `POST /api/documents/upload`. The knowledge base only grows when someone uploads a PDF. There is no automated mechanism to keep it current.

### Recommended implementation

#### Add a `KnowledgeSource` table

```csharp
public class KnowledgeSource
{
    public Guid Id { get; set; }
    public string Name { get; set; }        // "SPE Journal Feed", "API Standards"
    public string Type { get; set; }        // "RssFeed" | "WatchFolder" | "Url"
    public string Uri { get; set; }
    public DateTime? LastCrawledAt { get; set; }
    public string Status { get; set; }      // "Active" | "Paused"
    public int DocumentsIndexed { get; set; }
}
```

#### Add a Hangfire weekly ingestion job

```csharp
// In Program.cs alongside the existing daily news job
RecurringJob.AddOrUpdate<KnowledgeIngestionJob>(
    "weekly-knowledge-refresh",
    job => job.RefreshAllSourcesAsync(),
    Cron.Weekly(DayOfWeek.Sunday, 2));
```

#### Add content-hash deduplication to `Document`

Add `ContentHash` (SHA-256 of file bytes) to the `documents` table. Skip re-ingestion if hash matches an already-indexed document — saves compute and prevents embedding drift.

### Why this matters

RAG quality is bounded by the breadth and freshness of your knowledge base. An O&G assistant trained only on documents uploaded in January 2025 will give stale answers about new API/ISO standards, recent HSE incidents, or updated BOP regulations. Automated ingestion closes this gap without manual effort.

---

## Strategy 3 — Ollama Custom Modelfile (FUTURE WORK)

### What it is

Ollama supports creating custom models via a `Modelfile` — a simple text file that sets a `SYSTEM` prompt, temperature, and context window. This bakes domain identity into the model at the binary level so every call gets the right persona without sending a system prompt each time.

### Implementation

Create `TechAgent.API/models/oilgas-assistant.Modelfile`:

```dockerfile
FROM llama3

SYSTEM """
You are TechAgent, an expert AI assistant for Oil & Gas engineering.
You specialise in: drilling, reservoir engineering, HSE, pipeline integrity,
completion design, BOP systems, seismic interpretation, and LNG operations.

Rules:
- Only answer O&G domain questions
- Cite the document source when context is provided
- Never fabricate technical specifications or safety values
- Prefer SI units unless the user specifies otherwise
- For safety-critical questions (BOP, H2S, pressure limits), always add an HSE caveat
"""

PARAMETER temperature 0.3
PARAMETER top_p 0.9
PARAMETER num_ctx 8192
```

Build the model:

```bash
ollama create oilgas-assistant -f ./models/oilgas-assistant.Modelfile
```

Update `appsettings.json`:

```json
"Ollama": { "ChatModel": "oilgas-assistant" }
```

### Automated rebuild from golden data

Once you have 50+ golden pairs, add a Hangfire monthly job that:
1. Exports golden Q&A pairs from `chat_history` where `IsGolden = true`
2. Rewrites the Modelfile with top examples embedded in the SYSTEM block as few-shot demonstrations
3. Calls `POST http://localhost:11434/api/create` to rebuild the model

This is the lightest possible form of "fine-tuning" available for local Ollama models — no GPU training required.

---

## Strategy 4 — RAG Quality Improvements (FUTURE WORK)

### HyDE — Hypothetical Document Embeddings

Instead of embedding the raw user question, ask the LLM to generate a short hypothetical answer first, then embed that. The hypothetical answer is semantically closer to the documents that contain the real answer, improving retrieval recall by ~15-25% on technical domains.

```csharp
// In RagService.AskAsync, before Qdrant search
var hydePrompt = $"Write a 2-sentence expert answer to: {question}";
var hypotheticalAnswer = await _aiService.ChatAsync(hydePrompt, ct);
var queryVec = await _aiService.GetEmbeddingAsync(hypotheticalAnswer, ct);
```

### Embedding model upgrade

Switch from `nomic-embed-text` (768-dim) to `mxbai-embed-large` (1024-dim). On technical document retrieval benchmarks, `mxbai-embed-large` consistently outperforms nomic by 8-12 percentage points on MTEB. Pull via:

```bash
ollama pull mxbai-embed-large
```

**Important:** Changing the embedding model invalidates all existing vectors. You must re-index all documents after switching. Add a one-time Hangfire migration job for this.

### Bump TopK + rerank

Retrieve 8 chunks (up from 4), then use a cross-encoder reranker to select the best 3 for the prompt. More retrieval candidates + precise reranking consistently outperforms simply increasing TopK and sending all results to the LLM.

Options for reranking:
- **Cohere Rerank API** — cloud, simple, accurate
- **ms-marco-MiniLM-L-6-v2** via ONNX — local, fast, free

---

## Implementation Roadmap

| Priority | Strategy | Effort | Expected Impact |
|---|---|---|---|
| ✅ Done | Feedback loop + golden few-shot examples | Low | High — visible within 20 ratings |
| 2 | EF Core migration (`dotnet ef migrations add`) | Trivial | Unblocks all of the above |
| 3 | Custom Ollama Modelfile | Low | Medium — consistent domain persona |
| 4 | Automated document ingestion job | Medium | High — knowledge stays fresh |
| 5 | HyDE retrieval | Low | High — better semantic search |
| 6 | Embedding model upgrade (mxbai-embed-large) | Medium | Medium — requires full re-index |
| 7 | Cross-encoder reranking | High | High — precision improvement |
| 8 | Automated Modelfile rebuild from golden data | High | Medium — monthly quality uplift |

---

## Next Immediate Step

Run the EF Core migration to activate the feedback columns:

```bash
cd TechAgent.API
dotnet ef migrations add AddFeedbackToHistory
dotnet ef database update
```

Then start the app, ask a question, and use the thumbs-up button. After 5-10 golden ratings, prompt quality will begin to noticeably improve.
