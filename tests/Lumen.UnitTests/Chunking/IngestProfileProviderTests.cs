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

    [Fact]
    public void Table_switches_add_tokens_only_when_on()
    {
        Assert.Equal("chunk=250/60;hdr=v1;merge=0;dec=v1", Profile(new ChunkingOptions { ContextHeaders = true, ProtectDecimals = true }));
        Assert.Equal("chunk=250/60;hdr=v1;merge=0;dec=v1;cells=v2;thdr=v2;tcap=v2;tser=kv2", Profile(new ChunkingOptions
        {
            ContextHeaders = true, ProtectDecimals = true, TableCells = true, TableHeaders = true, TableCaptions = true, TableFormat = TableFormat.KeyValue
        }));
        Assert.Equal("chunk=250/60;hdr=off;merge=0;dec=off;tcap=v2", Profile(new ChunkingOptions { TableCaptions = true }));
    }
}
