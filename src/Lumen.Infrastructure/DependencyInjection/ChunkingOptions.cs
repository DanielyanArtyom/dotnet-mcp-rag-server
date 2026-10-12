namespace Lumen.Infrastructure.DependencyInjection;

public class ChunkingOptions
{
    public const string SectionName = "Chunking";
    public int MaxTokensPerChunk { get; set; } = 250;
    public int OverlapTokens { get; set; } = 60;
    public bool ContextHeaders { get; set; }
    public int MergeBelowTokens { get; set; }
    public bool ProtectDecimals { get; set; }
    public bool TableCells { get; set; }
    public bool TableHeaders { get; set; }
    public bool TableCaptions { get; set; }
    public TableFormat TableFormat { get; set; } = TableFormat.Pipe;
}
