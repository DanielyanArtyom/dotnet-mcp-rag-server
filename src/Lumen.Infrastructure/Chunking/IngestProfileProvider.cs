namespace Lumen.Infrastructure.Chunking;

public class IngestProfileProvider : IIngestProfileProvider
{
    public IngestProfileProvider(IOptions<ChunkingOptions> options)
    {
        var o = options.Value;
        Current = $"chunk={o.MaxTokensPerChunk}/{o.OverlapTokens};hdr={(o.ContextHeaders ? "v1" : "off")};merge={o.MergeBelowTokens};dec={(o.ProtectDecimals ? "v1" : "off")}";
    }

    public string Current { get; }
}
