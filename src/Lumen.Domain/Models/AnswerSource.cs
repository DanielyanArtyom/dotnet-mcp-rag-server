namespace Lumen.Domain.Models;

public class AnswerSource
{
    public int SourceId { get; set; }
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = default!;
    public int ChunkIndex { get; set; }
    public ChunkType ChunkType { get; set; }
    public int TokenCount { get; set; }
    public double Distance { get; set; }
    public double? Score { get; set; }
    public bool Cited { get; set; }
    public string Text { get; set; } = default!;
}
