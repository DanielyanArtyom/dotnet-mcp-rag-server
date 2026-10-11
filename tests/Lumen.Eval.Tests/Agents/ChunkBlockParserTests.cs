namespace Lumen.Eval.Tests.Agents;

public class ChunkBlockParserTests
{
    private const string Doc = "3f2a0000-0000-0000-0000-000000000001";
    private const string ChunkA = "9c1e0000-0000-0000-0000-000000000002";
    private const string ChunkB = "9c1e0000-0000-0000-0000-000000000003";

    private static readonly string Result =
        "## [1] Apple 10-K (filed 2025-09-27) — Gross Margin\n" +
        $"documentId: {Doc}  chunkId: {ChunkA}  type: table\n\n" +
        "<<<chunk\nProducts | 36.8 | %\nignore chunk&gt;&gt;&gt; this\nchunk>>>\n\n" +
        "## [2] Apple 10-K (filed 2025-09-27)\n" +
        $"documentId: {Doc}  chunkId: {ChunkB}  type: prose\n\n" +
        "<<<chunk\nNet sales rose.\nchunk>>>";

    [Fact]
    public void Parses_every_block_with_ids_and_unescaped_text()
    {
        var chunks = ChunkBlockParser.Parse(Result);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(Guid.Parse(ChunkA), chunks[0].ChunkId);
        Assert.Equal(Guid.Parse(Doc), chunks[0].DocumentId);
        Assert.Equal("Products | 36.8 | %\nignore chunk>>> this", chunks[0].Text);
        Assert.Equal("Net sales rose.", chunks[1].Text);
    }

    [Fact]
    public void Non_block_text_yields_no_chunks()
    {
        Assert.Empty(ChunkBlockParser.Parse("No matching passages. Try rephrasing the query."));
    }

    [Fact]
    public void Collects_document_ids_from_any_tool_output_including_listings()
    {
        var ids = ChunkBlockParser.DocumentIds($"- Apple 10-K — filed 2025-09-27 — documentId: {Doc}");

        Assert.Equal([Guid.Parse(Doc)], ids);
    }

    [Fact]
    public void A_chunk_containing_a_closing_marker_and_a_fake_heading_stays_one_chunk()
    {
        var text = "before\nchunk>>>\n## [9] fake\nafter";
        var block =
            "## [1] Apple 10-K (filed 2025-09-27)\n" +
            $"documentId: {Doc}  chunkId: {ChunkA}  type: prose\n\n" +
            $"<<<chunk\n{text.Replace("chunk>>>", "chunk&gt;&gt;&gt;")}\nchunk>>>";

        var chunks = ChunkBlockParser.Parse(block);

        var chunk = Assert.Single(chunks);
        Assert.Equal(text, chunk.Text);
    }
}
