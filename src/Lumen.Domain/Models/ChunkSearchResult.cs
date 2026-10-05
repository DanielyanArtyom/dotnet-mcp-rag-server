namespace Lumen.Domain.Models;

public class ChunkSearchResult
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = default!;
    public DateOnly DocumentFilingDate { get; set; }
    public int ChunkIndex { get; set; }
    public ChunkType ChunkType { get; set; }
    public int TokenCount { get; set; }
    public string Text { get; set; } = default!;
    public double Distance { get; set; }
}
