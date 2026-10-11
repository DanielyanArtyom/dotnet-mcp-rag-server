namespace Lumen.Eval.Agents;

public record ParsedChunk(Guid DocumentId, Guid ChunkId, string Text);

public static class ChunkBlockParser
{
    private static readonly Regex Block = new(
        @"documentId: (?<doc>[0-9a-fA-F-]{36})\s+chunkId: (?<chunk>[0-9a-fA-F-]{36})[^\n]*\n\n<<<chunk\n(?<text>.*?)\nchunk>>>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex DocumentId = new(@"documentId: (?<doc>[0-9a-fA-F-]{36})", RegexOptions.CultureInvariant);

    public static List<ParsedChunk> Parse(string toolResult) =>
        Block.Matches(toolResult).Select(m => new ParsedChunk(
            Guid.Parse(m.Groups["doc"].Value),
            Guid.Parse(m.Groups["chunk"].Value),
            m.Groups["text"].Value.Replace("chunk&gt;&gt;&gt;", "chunk>>>"))).ToList();

    public static List<Guid> DocumentIds(string toolResult) =>
        DocumentId.Matches(toolResult).Select(m => Guid.Parse(m.Groups["doc"].Value)).Distinct().ToList();
}
