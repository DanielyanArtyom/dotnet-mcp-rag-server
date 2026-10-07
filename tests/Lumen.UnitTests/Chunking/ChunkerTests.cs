namespace Lumen.UnitTests.Chunking;

public class ChunkerTests
{
    private static readonly ChunkingContext Context = new("Doc", new DateOnly(2025, 9, 27));

    private static Chunker CreateChunker(int maxTokens = 250, int overlapTokens = 60, bool contextHeaders = false, int mergeBelowTokens = 0, bool protectDecimals = false) =>
        new(Options.Create(new ChunkingOptions
        {
            MaxTokensPerChunk = maxTokens,
            OverlapTokens = overlapTokens,
            ContextHeaders = contextHeaders,
            MergeBelowTokens = mergeBelowTokens,
            ProtectDecimals = protectDecimals
        }));

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

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

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

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

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

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
    }

    [Fact]
    public void Chunk_splits_an_oversized_table_into_multiple_table_chunks()
    {
        var chunker = CreateChunker(maxTokens: 20, overlapTokens: 0);
        var manyRows = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Row {i} | Value {i}"));
        var elements = new List<DocumentElement> { new() { Type = ElementType.Table, Text = manyRows } };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

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

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

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

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.All(chunks, c => Assert.Equal(ChunkType.Table, c.ChunkType));
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 20, $"chunk has {c.TokenCount} tokens"));
        static string Normalize(string s) => string.Concat(s.Where(ch => !char.IsWhiteSpace(ch)));
        Assert.Equal(Normalize(text), Normalize(string.Concat(chunks.Select(c => c.Text))));
    }

    [Fact]
    public void Chunk_returns_no_chunks_for_an_empty_element_list()
    {
        Assert.Empty(CreateChunker().Chunk(Guid.NewGuid(), [], Context));
    }

    [Fact]
    public void Chunk_skips_whitespace_only_elements()
    {
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "  \n " },
            new() { Type = ElementType.Table, Text = "   " }
        };

        Assert.Empty(CreateChunker().Chunk(Guid.NewGuid(), elements, Context));
    }

    [Fact]
    public void Chunk_handles_a_table_only_document()
    {
        var elements = new List<DocumentElement> { new() { Type = ElementType.Table, Text = "A | 1\nB | 2" } };

        var chunk = Assert.Single(CreateChunker().Chunk(Guid.NewGuid(), elements, Context));

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

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.Equal(ChunkType.Prose, c.ChunkType));
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250, $"chunk has {c.TokenCount} tokens"));
    }

    [Fact]
    public void Chunk_keeps_a_single_very_long_paragraph_within_the_token_budget()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement> { new() { Type = ElementType.Paragraph, Text = Sentences(3000) } };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

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

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.True(chunks.Count > 100);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250));
    }

    [Fact]
    public void Headers_are_off_by_default()
    {
        var chunks = CreateChunker().Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = "Net sales rose." }], Context);

        Assert.Null(Assert.Single(chunks).ContextHeader);
    }

    [Fact]
    public void Each_chunk_gets_the_section_that_precedes_it()
    {
        var chunker = CreateChunker(contextHeaders: true);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "Gross Margin" },
            new() { Type = ElementType.Paragraph, Text = "Gross margin increased on a favorable mix." },
            new() { Type = ElementType.Paragraph, Text = "Operating Expenses" },
            new() { Type = ElementType.Paragraph, Text = "Operating expenses for 2025 were as follows:" },
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550" }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.Equal("Doc (filed 2025-09-27) — Gross Margin", chunks[0].ContextHeader);
        Assert.Contains("favorable mix", chunks[0].Text);
        Assert.DoesNotContain("Operating", chunks[0].Text);
        Assert.Equal("Doc (filed 2025-09-27) — Operating Expenses", chunks.Single(c => c.ChunkType == ChunkType.Table).ContextHeader);
        Assert.All(chunks, c => Assert.DoesNotContain("filed", c.Text));
    }

    [Fact]
    public void Header_tokens_do_not_count_against_the_budget()
    {
        var chunker = CreateChunker(maxTokens: 50, overlapTokens: 0, contextHeaders: true);

        var chunks = chunker.Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = Sentences(400) }], Context);

        Assert.All(chunks, c => Assert.True(c.TokenCount <= 50));
    }

    [Fact]
    public void Merges_a_short_chunk_into_the_previous_one()
    {
        var chunker = CreateChunker(mergeBelowTokens: 20);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550 | $ | 31,370" },
            new() { Type = ElementType.Table, Text = "Apple Inc. | 2025 Form 10-K | 24" }
        };

        var chunk = Assert.Single(chunker.Chunk(Guid.NewGuid(), elements, Context));

        Assert.Contains("34,550", chunk.Text);
        Assert.EndsWith("Apple Inc. | 2025 Form 10-K | 24", chunk.Text);
        Assert.Equal(0, chunk.ChunkIndex);
    }

    [Fact]
    public void Merges_a_leading_short_chunk_into_the_next_one()
    {
        var chunker = CreateChunker(mergeBelowTokens: 20);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = "Page 1" },
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550 | $ | 31,370" }
        };

        var chunk = Assert.Single(chunker.Chunk(Guid.NewGuid(), elements, Context));

        Assert.StartsWith("Page 1", chunk.Text);
    }

    [Fact]
    public void Merging_keeps_the_receiving_chunks_header()
    {
        var chunker = CreateChunker(contextHeaders: true, mergeBelowTokens: 20);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550 | $ | 31,370" },
            new() { Type = ElementType.Table, Text = "Page 3" }
        };

        var chunk = Assert.Single(chunker.Chunk(Guid.NewGuid(), elements, Context));

        Assert.Equal(ChunkType.Table, chunk.ChunkType);
        Assert.NotNull(chunk.ContextHeader);
        Assert.DoesNotContain(chunk.ContextHeader!, chunk.Text);
    }

    [Fact]
    public void Keeps_a_short_chunk_when_neither_neighbour_has_room()
    {
        var chunker = CreateChunker(maxTokens: 30, overlapTokens: 0, mergeBelowTokens: 10);
        var full = string.Join(" | ", Enumerable.Range(1, 10).Select(i => $"{i}0"));
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = full },
            new() { Type = ElementType.Table, Text = "Page 2" },
            new() { Type = ElementType.Table, Text = full }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Page 2", chunks[1].Text);
        Assert.True(chunks[0].TokenCount + chunks[1].TokenCount + 1 > 30);
        Assert.True(chunks[1].TokenCount + chunks[2].TokenCount + 1 > 30);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 30));
        Assert.Equal(new[] { 0, 1, 2 }, chunks.Select(c => c.ChunkIndex));
    }

    [Fact]
    public void Merging_is_off_by_default()
    {
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550" },
            new() { Type = ElementType.Table, Text = "Page 3" }
        };

        Assert.Equal(2, CreateChunker().Chunk(Guid.NewGuid(), elements, Context).Count);
    }

    private static string LongLineWith(string number) =>
        string.Join(" ", Enumerable.Range(0, 30).Select(i => $"Item {i} rose by {number} billion compared with the prior period."));

    [Theory]
    [InlineData("$10.7")]
    [InlineData("$19.8")]
    [InlineData("4.00%")]
    [InlineData("$200.00 per share")]
    public void Protected_decimals_survive_line_splitting(string number)
    {
        var chunker = CreateChunker(maxTokens: 120, overlapTokens: 0, protectDecimals: true);

        var chunks = chunker.Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = LongLineWith(number) }], Context);

        var all = string.Join("\n", chunks.Select(c => c.Text));
        Assert.DoesNotMatch(@"\d\.\s+\d", all);
        Assert.DoesNotContain("\u2024", all);
        Assert.Contains(number, all);
    }

    [Fact]
    public void Without_protection_the_splitter_breaks_decimals()
    {
        var chunks = CreateChunker(maxTokens: 120, overlapTokens: 0).Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = LongLineWith("$10.7") }], Context);

        Assert.Matches(@"\d\.\s+\d", string.Join("\n", chunks.Select(c => c.Text)));
    }

    [Fact]
    public void Protected_decimals_keep_token_counts_exact_and_within_budget_with_merging_and_headers()
    {
        var chunker = CreateChunker(maxTokens: 120, overlapTokens: 0, contextHeaders: true, mergeBelowTokens: 20, protectDecimals: true);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Heading, Text = "Item 7. Management Discussion" },
            new() { Type = ElementType.Paragraph, Text = LongLineWith("$10.7") },
            new() { Type = ElementType.Table, Text = "Rate | 4.25%\nPage 9" }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.All(chunks, c =>
        {
            Assert.True(c.TokenCount <= 120);
            Assert.DoesNotContain("\u2024", c.Text);
        });
        Assert.Contains(chunks, c => c.Text.Contains("4.25%"));
    }

    [Fact]
    public void A_literal_one_dot_leader_character_is_untouched_when_decimal_protection_is_off()
    {
        var chunker = CreateChunker(protectDecimals: false);

        var chunk = Assert.Single(chunker.Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = "Section\u2024 one\u2024 end" }], Context));

        Assert.Equal("Section\u2024 one\u2024 end", chunk.Text);
    }

    [Theory]
    [InlineData(250, 0)]
    [InlineData(120, 0)]
    [InlineData(250, 60)]
    public void Every_prose_chunk_respects_the_token_budget(int maxTokens, int overlapTokens)
    {
        var chunker = CreateChunker(maxTokens, overlapTokens, protectDecimals: true);

        var chunks = chunker.Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = Sentences(6000) }], Context);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= maxTokens, $"{c.TokenCount} > {maxTokens}"));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
    }
}
