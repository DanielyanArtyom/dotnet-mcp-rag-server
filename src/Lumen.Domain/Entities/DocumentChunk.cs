namespace Lumen.Domain.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid TenantId { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = default!;
    public int TokenCount { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public ChunkType ChunkType { get; set; }
    public float[] Embedding { get; set; } = [];
    public string EmbeddingModel { get; set; } = string.Empty;
}
