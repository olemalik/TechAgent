namespace OilGasAI.API.Models;

// ── Ollama /api/embed (nomic-embed-text) ─────────────────────────────────────
// Sends text → receives 768-number vector representing meaning

public record OllamaEmbedRequest(string model, IReadOnlyList<string> input);
public record OllamaEmbedResponse(IReadOnlyList<float[]> embeddings);

// ── Ollama /api/generate (oilgas-assistant, stream:false) ────────────────────

public record OllamaGenerateRequest(string model, string prompt, bool stream);
public record OllamaGenerateResponse(string response, bool done);

// ── RAG pipeline request / response ─────────────────────────────────────────

public class RagChatRequest
{
    /// <summary>The user's Oil and Gas question.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional session ID — ties messages together in a conversation.
    /// Send null to start a new session; include the returned sessionId for follow-up messages.
    /// </summary>
    public Guid? SessionId { get; set; }

    public List<ChatMessage> History { get; set; } = new();
}

public class RagChatResponse
{
    public Guid SessionId { get; set; }
    public string Reply { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public bool WasRefused { get; set; }

    /// <summary>Names of company documents that were used to compose the answer.</summary>
    public List<string> Sources { get; set; } = new();

    public string? Error { get; set; }
}

// ── Document ingestion ────────────────────────────────────────────────────────

public class DocumentUploadResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class DocumentStatusResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;   // Processing | Indexed | Failed
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? IndexedAt { get; set; }
}

// ── Domain entities added to support RAG ─────────────────────────────────────

/// <summary>Metadata about an uploaded PDF document.</summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public int ChunkCount { get; set; }
    public string Status { get; set; } = "Processing";  // Processing | Indexed | Failed
    public string? ErrorMessage { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? IndexedAt { get; set; }

    public List<DocumentChunk> Chunks { get; set; } = new();
}

/// <summary>
/// Parent-Child chunking: child (small, 150 words) → embedded in Qdrant for precise search.
/// Parent (large, 600 words) → stored in PostgreSQL, injected into LLM for rich context.
/// The same Guid is used as both PostgreSQL PK and Qdrant point ID.
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string ParentText { get; set; } = null!;   // Injected into LLM context
    public string ChildText { get; set; } = null!;    // Embedded in Qdrant for search
    public int ChunkIndex { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Document Document { get; set; } = null!;
}

/// <summary>Persisted conversation history per session.</summary>
public class ChatSessionHistory
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = null!;          // user | assistant
    public string Message { get; set; } = null!;
    public bool WasRefused { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}