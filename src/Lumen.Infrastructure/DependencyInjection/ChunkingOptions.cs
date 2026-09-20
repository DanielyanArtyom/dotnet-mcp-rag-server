namespace Lumen.Infrastructure.DependencyInjection;

public class ChunkingOptions
{
    public const string SectionName = "Chunking";
    public int MaxTokensPerChunk { get; set; } = 250;
    public int OverlapTokens { get; set; } = 60;
}
