namespace Lumen.UnitTests.Chunking;

public class ChunkerTests
{
    private static Chunker CreateChunker(int maxTokens = 250, int overlapTokens = 60) =>
        new(Options.Create(new ChunkingOptions { MaxTokensPerChunk = maxTokens, OverlapTokens = overlapTokens }));

    private static string Sentences(int words) =>
        string.Join(" ", Enumerable.Range(0, words / 8).Select(i => $"Net sales increased {i} percent due to strong Services."));

    [Fact]
    public void Chunk_produces_prose_chunks_for_a_short_document()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Heading, Text = "Item 7. MD&A" },
            new() { Type = ElementType.Paragraph, Text = "Net sales increased due to strength in Services and Mac." }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        var chunk = Assert.Single(chunks);
        Assert.Equal(ChunkType.Prose, chunk.ChunkType);
        Assert.Contains("Item 7. MD&A", chunk.Text);
        Assert.Contains("Net sales increased due to strength in Services and Mac.", chunk.Text);
        Assert.True(chunk.TokenCount > 0);
    }

    [Fact]
    public void Chunk_never_merges_a_table_with_surrounding_prose()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "Segment net sales for the quarter were as follows." },
            new() { Type = ElementType.Table, Text = "Americas | 45781\nEurope | 29395" },
            new() { Type = ElementType.Paragraph, Text = "Americas net sales increased primarily due to iPhone." }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        var tableChunks = chunks.Where(c => c.ChunkType == ChunkType.Table).ToList();
        Assert.Single(tableChunks);
        Assert.Contains("45781", tableChunks[0].Text);
        Assert.DoesNotContain("Segment net sales", tableChunks[0].Text);
        Assert.DoesNotContain("iPhone", tableChunks[0].Text);
    }

    [Fact]
    public void Chunk_assigns_sequential_ChunkIndex_values()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "First paragraph." },
            new() { Type = ElementType.Table, Text = "A | 1" },
            new() { Type = ElementType.Paragraph, Text = "Last paragraph." }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
    }

    [Fact]
    public void Chunk_splits_an_oversized_table_into_multiple_table_chunks()
    {
        var chunker = CreateChunker(maxTokens: 20, overlapTokens: 0);
        var manyRows = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Row {i} | Value {i}"));
        var elements = new List<DocumentElement> { new() { Type = ElementType.Table, Text = manyRows } };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(ChunkType.Table, c.ChunkType));
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 20, $"chunk has {c.TokenCount} tokens"));
        Assert.Equal(manyRows, string.Join("\n", chunks.Select(c => c.Text)));
    }

    [Fact]
    public void Chunk_splits_a_table_of_many_tiny_rows_within_the_budget()
    {
        var chunker = CreateChunker(maxTokens: 20, overlapTokens: 0);
        var rows = string.Join("\n", Enumerable.Range(0, 300).Select(i => $"{i} | 1"));
        var elements = new List<DocumentElement> { new() { Type = ElementType.Table, Text = rows } };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 20, $"chunk has {c.TokenCount} tokens"));
        Assert.Equal(rows, string.Join("\n", chunks.Select(c => c.Text)));
    }

    [Fact]
    public void Chunk_splits_a_single_huge_table_row_within_the_budget()
    {
        var chunker = CreateChunker(maxTokens: 20, overlapTokens: 0);
        var hugeRow = string.Join(" | ", Enumerable.Range(0, 200).Select(i => $"Cell{i}"));
        var text = "Header | Value\n" + hugeRow + "\nTotal | 9";
        var elements = new List<DocumentElement> { new() { Type = ElementType.Table, Text = text } };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.All(chunks, c => Assert.Equal(ChunkType.Table, c.ChunkType));
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 20, $"chunk has {c.TokenCount} tokens"));
        static string Normalize(string s) => string.Concat(s.Where(ch => !char.IsWhiteSpace(ch)));
        Assert.Equal(Normalize(text), Normalize(string.Concat(chunks.Select(c => c.Text))));
    }

    [Fact]
    public void Chunk_returns_no_chunks_for_an_empty_element_list()
    {
        Assert.Empty(CreateChunker().Chunk(Guid.NewGuid(), []));
    }

    [Fact]
    public void Chunk_skips_whitespace_only_elements()
    {
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "  \n " },
            new() { Type = ElementType.Table, Text = "   " }
        };

        Assert.Empty(CreateChunker().Chunk(Guid.NewGuid(), elements));
    }

    [Fact]
    public void Chunk_handles_a_table_only_document()
    {
        var elements = new List<DocumentElement> { new() { Type = ElementType.Table, Text = "A | 1\nB | 2" } };

        var chunk = Assert.Single(CreateChunker().Chunk(Guid.NewGuid(), elements));

        Assert.Equal(ChunkType.Table, chunk.ChunkType);
        Assert.Equal("A | 1\nB | 2", chunk.Text);
    }

    [Fact]
    public void Chunk_keeps_a_long_multi_paragraph_prose_run_within_the_token_budget()
    {
        var chunker = CreateChunker();
        var elements = Enumerable.Range(0, 10)
            .Select(_ => new DocumentElement { Type = ElementType.Paragraph, Text = Sentences(500) })
            .ToList();

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.Equal(ChunkType.Prose, c.ChunkType));
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250, $"chunk has {c.TokenCount} tokens"));
    }

    [Fact]
    public void Chunk_keeps_a_single_very_long_paragraph_within_the_token_budget()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement> { new() { Type = ElementType.Paragraph, Text = Sentences(3000) } };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250, $"chunk has {c.TokenCount} tokens"));
    }

    [Fact]
    public void Chunk_completes_on_a_very_large_prose_run()
    {
        var chunker = CreateChunker();
        var elements = Enumerable.Range(0, 100)
            .Select(_ => new DocumentElement { Type = ElementType.Paragraph, Text = Sentences(500) })
            .ToList();

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.True(chunks.Count > 100);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250));
    }
}
