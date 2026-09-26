namespace Lumen.Infrastructure.Repositories;

public class ChunkSearchRow
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = default!;
    public int ChunkIndex { get; set; }
    public int ChunkType { get; set; }
    public int TokenCount { get; set; }
    public string Text { get; set; } = default!;
    public double Distance { get; set; }
}
