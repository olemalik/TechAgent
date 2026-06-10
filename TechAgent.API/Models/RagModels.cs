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

// ── Supporting chat types ─────────────────────────────────────────────────────

public record ChatMessage(string Role, string Content);

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessage> History { get; set; } = new();
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
}

// ── Domain entities added to support RAG ─────────────────────────────────────


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