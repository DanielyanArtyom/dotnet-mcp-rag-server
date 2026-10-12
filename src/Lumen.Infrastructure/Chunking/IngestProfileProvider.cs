namespace Lumen.Infrastructure.Chunking;

public class IngestProfileProvider : IIngestProfileProvider
{
    public IngestProfileProvider(IOptions<ChunkingOptions> options)
    {
        var o = options.Value;
        var profile = new StringBuilder($"chunk={o.MaxTokensPerChunk}/{o.OverlapTokens};hdr={(o.ContextHeaders ? "v1" : "off")};merge={o.MergeBelowTokens};dec={(o.ProtectDecimals ? "v1" : "off")}");
        // Table switches are appended only when on, so a corpus ingested before they existed keeps a matching profile.
        if (o.TableCells)
        {
            profile.Append(";cells=v2");
        }

        if (o.TableHeaders)
        {
            profile.Append(";thdr=v2");
        }

        if (o.TableCaptions)
        {
            profile.Append(";tcap=v2");
        }

        if (o.TableFormat == TableFormat.KeyValue)
        {
            profile.Append(";tser=kv2");
        }

        Current = profile.ToString();
    }

    public string Current { get; }
}
