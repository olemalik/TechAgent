using Microsoft.Extensions.AI;

namespace OilGasAI.API.Models;

// ── Ollama /api/embed (nomic-embed-text) ─────────────────────────────────────
// Sends text → receives 768-number vector representing meaning

public record OllamaEmbedRequest(string model, IReadOnlyList<string> input);
public record OllamaEmbedResponse(IReadOnlyList<float[]> embeddings);

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

    /// <summary>Optional file attached by the user — returned from POST /api/files/upload.</summary>
    public string? AttachmentName { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? AttachmentContentType { get; set; }
}

/// <summary>Returned by POST /api/files/upload.</summary>
public class FileUploadResponse
{
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
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

    /// <summary>DB row ID of the assistant message — used by the client to submit feedback.</summary>
    public long? AssistantMessageId { get; set; }
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
    public string Status { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? IndexedAt { get; set; }
    // Populated only when Status = PendingReview
    public List<DocumentConflict>? Conflicts { get; set; }
}

/// <summary>A document that is semantically similar to the newly uploaded one.</summary>
public class DocumentConflict
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    /// <summary>Cosine similarity 0–1 between full-document centroids. Shown as % in the UI.</summary>
    public float Similarity { get; set; }
}

public class ResolveConflictRequest
{
    /// <summary>"replace" | "keep-both" | "cancel"</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Document IDs to supersede. Required when Action = "replace".</summary>
    public List<Guid>? ReplaceIds { get; set; }
}

// ── Streaming chunk ───────────────────────────────────────────────────────────

public class RagStreamChunk
{
    /// <summary>"token" while streaming, "done" when the stream is complete, "error" on failure.</summary>
    public string Type { get; set; } = "token";
    public string? Value { get; set; }
    public Guid? SessionId { get; set; }
    public bool WasRefused { get; set; }

    /// <summary>DB row ID of the assistant message — present only on the "done" event.</summary>
    public long? AssistantMessageId { get; set; }
}

// ── Feedback ──────────────────────────────────────────────────────────────────

public class FeedbackRequest
{
    /// <summary>DB row ID of the assistant message being rated.</summary>
    public long MessageId { get; set; }

    /// <summary>1 = thumbs up, -1 = thumbs down.</summary>
    public int Score { get; set; }

    /// <summary>Optional correction the user provides when the answer was wrong.</summary>
    public string? Correction { get; set; }
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
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Feedback loop — populated after user rates an assistant message
    public int? FeedbackScore { get; set; }           // 1 = thumbs up, -1 = thumbs down
    public string? UserCorrection { get; set; }       // optional correction text from user
    public bool IsGolden { get; set; }                // promoted to few-shot example pool

    // File attachment — optional, populated when the user attaches a file to their message
    public string? AttachmentName { get; set; }       // original file name shown in UI
    public string? AttachmentUrl { get; set; }        // served path e.g. /uploads/guid.pdf
    public string? AttachmentContentType { get; set; }
}