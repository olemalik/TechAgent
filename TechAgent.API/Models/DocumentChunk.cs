// Models/DocumentChunk.cs
namespace OilGasAI.API.Models;
/// <summary>
/// Parent-Child chunking: child (small, 150 words) → embedded in Qdrant for precise search.
/// Parent (large, 600 words) → stored in PostgreSQL, injected into LLM for rich context.
/// The same Guid is used as both PostgreSQL PK and Qdrant point ID.
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string ChildText { get; set; } = string.Empty;
    public string ParentText { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }

    public Document Document { get; set; } = null!;
}