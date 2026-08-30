using Microsoft.EntityFrameworkCore;
using OilGasAI.API.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<SentNews> SentNews { get; set; }

    // Documents: metadata for every uploaded PDF
    public DbSet<Document> Documents { get; set; }

    // DocumentChunks: parent text (600 words) stored here; child embeddings live in Qdrant
    public DbSet<DocumentChunk> DocumentChunks { get; set; }

    // ChatSessionHistory: conversation history per session (last 8 turns sent to Ollama)
    public DbSet<ChatSessionHistory> ChatHistory { get; set; }

    // McpServerConfigs: user-configured MCP server connections
    public DbSet<McpServerConfig> McpServerConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SentNews>()
            .HasIndex(x => x.Title)
            .IsUnique();

        // ── Document ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Document>(b =>
        {
            b.ToTable("documents");
            b.HasKey(d => d.Id);
            b.Property(d => d.FileName).HasMaxLength(400).IsRequired();
            b.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
            b.Property(d => d.Status).HasMaxLength(20).IsRequired();
            b.Property(d => d.ErrorMessage).HasMaxLength(1000);
            b.HasIndex(d => d.Status);

            b.HasMany(d => d.Chunks)
                .WithOne(c => c.Document)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── DocumentChunk ─────────────────────────────────────────────────────
        modelBuilder.Entity<DocumentChunk>(b =>
        {
            b.ToTable("document_chunks");
            b.HasKey(c => c.Id);
            b.Property(c => c.ParentText).IsRequired();
            b.Property(c => c.ChildText).IsRequired();
            b.HasIndex(c => c.DocumentId);
        });

        // ── ChatSessionHistory ────────────────────────────────────────────────
        modelBuilder.Entity<ChatSessionHistory>(b =>
        {
            b.ToTable("chat_history");
            b.HasKey(h => h.Id);
            b.Property(h => h.Role).HasMaxLength(16).IsRequired();
            b.Property(h => h.Message).IsRequired();
            b.Property(h => h.AttachmentName).HasMaxLength(500);
            b.Property(h => h.AttachmentUrl).HasMaxLength(1000);
            b.Property(h => h.AttachmentContentType).HasMaxLength(100);
            b.HasIndex(h => new { h.SessionId, h.CreatedAt });
        });

        // ── McpServerConfig ───────────────────────────────────────────────────
        modelBuilder.Entity<McpServerConfig>(b =>
        {
            b.ToTable("mcp_server_configs");
            b.HasKey(m => m.Id);
            b.Property(m => m.Name).HasMaxLength(200).IsRequired();
            b.Property(m => m.TransportType).HasMaxLength(20).IsRequired();
            b.Property(m => m.Url).HasMaxLength(1000);
            b.Property(m => m.ApiKey).HasMaxLength(500);
            b.Property(m => m.Description).HasMaxLength(500);
        });
    }
}