using Lumen.Domain.Entities;

namespace Lumen.Domain.Models;

public class ChunkContext
{
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = default!;
    public DateOnly DocumentFilingDate { get; set; }
    public Guid RequestedChunkId { get; set; }
    public List<DocumentChunk> Chunks { get; set; } = [];
}
