namespace Lumen.UnitTests.Chunking;

public class IngestProfileProviderTests
{
    private static string Profile(ChunkingOptions options) => new IngestProfileProvider(Options.Create(options)).Current;

    [Fact]
    public void All_switches_off_by_default() =>
        Assert.Equal("chunk=250/60;hdr=off;merge=0;dec=off", Profile(new ChunkingOptions()));

    [Fact]
    public void Every_switch_is_reflected() =>
        Assert.Equal("chunk=400/100;hdr=v1;merge=20;dec=v1", Profile(new ChunkingOptions
        {
            MaxTokensPerChunk = 400, OverlapTokens = 100, ContextHeaders = true, MergeBelowTokens = 20, ProtectDecimals = true
        }));
}
