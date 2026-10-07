namespace Lumen.Infrastructure.Persistence.Entities;

public class DocumentChunkEntity : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = default!;
    public int TokenCount { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public ChunkType ChunkType { get; set; }
    public Vector Embedding { get; set; } = default!;
    public string EmbeddingModel { get; set; } = default!;
    public string? ContextHeader { get; set; }
}
