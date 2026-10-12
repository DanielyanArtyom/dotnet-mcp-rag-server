namespace Lumen.UnitTests.Chunking;

public class ChunkerTests
{
    private static readonly ChunkingContext Context = new("Doc", new DateOnly(2025, 9, 27));

    private static Chunker CreateChunker(int maxTokens = 250, int overlapTokens = 60, bool contextHeaders = false, int mergeBelowTokens = 0,
        bool protectDecimals = false, bool tableCells = false, bool tableHeaders = false, bool tableCaptions = false, TableFormat tableFormat = TableFormat.Pipe, ILogger<Chunker>? logger = null) =>
        new(Options.Create(new ChunkingOptions
        {
            MaxTokensPerChunk = maxTokens,
            OverlapTokens = overlapTokens,
            ContextHeaders = contextHeaders,
            MergeBelowTokens = mergeBelowTokens,
            ProtectDecimals = protectDecimals,
            TableCells = tableCells,
            TableHeaders = tableHeaders,
            TableCaptions = tableCaptions,
            TableFormat = tableFormat
        }), logger ?? NullLogger<Chunker>.Instance);

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

    [Fact]
    public async Task With_table_switches_off_a_grid_carrying_table_chunks_exactly_as_before()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");
        var gridless = new DocumentElement { Type = ElementType.Table, Text = element.Text };

        var withGrid = CreateChunker(protectDecimals: true).Chunk(Guid.NewGuid(), [element], Context);
        var withoutGrid = CreateChunker(protectDecimals: true).Chunk(Guid.NewGuid(), [gridless], Context);

        Assert.Equal(withoutGrid.Select(c => c.Text), withGrid.Select(c => c.Text));
    }

    [Fact]
    public async Task Continuation_chunks_repeat_the_header_and_the_open_row_group()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");
        var header = TableFormatter.Format(element, null, new ChunkingOptions { TableCells = true }).HeaderLines;

        var chunks = CreateChunker(tableCells: true, tableHeaders: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.StartsWith(string.Join("\n", header), c.Text));
        var cet1 = Assert.Single(chunks, c => c.Text.Contains("CET1 capital | 14.2%"));
        Assert.Contains("Capital ratios - Standardized(a)", cet1.Text);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250));
    }

    [Fact]
    public async Task Without_TableHeaders_only_the_first_chunk_has_the_header()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");

        var chunks = CreateChunker(tableCells: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.StartsWith("Financial performance of JPMorganChase", chunks[0].Text);
        Assert.DoesNotContain(chunks.Skip(1), c => c.Text.StartsWith("Financial performance of JPMorganChase"));
    }

    [Fact]
    public void A_row_group_label_never_ends_a_table_chunk()
    {
        var rows = Enumerable.Range(0, 60).Select(i => new TableRow { Cells = [new TableCell { Text = $"Line item {i}" }, new TableCell { Text = $"{i},000" }] }).ToList();
        rows.Insert(0, new TableRow { Cells = [new TableCell { Text = "" }, new TableCell { Text = "2026" }] });
        for (var i = 10; i < rows.Count; i += 10)
            rows.Insert(i, new TableRow { Cells = [new TableCell { Text = $"Group {i}" }] });
        var element = new DocumentElement { Type = ElementType.Table, Text = "x", Table = new TableGrid { Rows = rows } };

        var chunks = CreateChunker(maxTokens: 60, overlapTokens: 0, tableCells: true, tableHeaders: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.True(chunks.Count > 3);
        Assert.DoesNotContain(chunks, c => c.Text.Split('\n')[^1].StartsWith("Group "));
    }

    [Fact]
    public async Task A_header_bigger_than_half_the_budget_is_not_repeated_and_the_budget_holds()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");

        var chunks = CreateChunker(maxTokens: 80, overlapTokens: 0, tableCells: true, tableHeaders: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.All(chunks, c => Assert.True(c.TokenCount <= 80, $"{c.TokenCount} tokens"));
        Assert.Contains(chunks.Skip(1), c => !c.Text.StartsWith("Financial performance of JPMorganChase"));
    }

    [Fact]
    public async Task The_caption_is_copied_into_every_table_chunk_and_stays_in_the_prose()
    {
        var caption = new DocumentElement { Type = ElementType.Paragraph, Text = "The following table summarizes selected financial performance:" };
        var table = await TableFixtures.LoadAsync("jpm-financial-performance");

        var chunks = CreateChunker(tableCells: true, tableHeaders: true, tableCaptions: true).Chunk(Guid.NewGuid(), [caption, table], Context);

        Assert.Contains("The following table summarizes", chunks[0].Text);
        Assert.Equal(ChunkType.Prose, chunks[0].ChunkType);
        Assert.All(chunks.Where(c => c.ChunkType == ChunkType.Table),
            c => Assert.StartsWith("Table: The following table summarizes selected financial performance:", c.Text));
    }

    [Fact]
    public void A_pdf_table_without_a_grid_gets_its_caption_and_keeps_decimals_whole()
    {
        var caption = new DocumentElement { Type = ElementType.Paragraph, Text = "Revenue by segment was as follows:" };
        var table = new DocumentElement { Type = ElementType.Table, Text = "Segment | 2025\nCloud | 10.7" };

        var chunks = CreateChunker(protectDecimals: true, tableCells: true, tableHeaders: true, tableCaptions: true, tableFormat: TableFormat.KeyValue)
            .Chunk(Guid.NewGuid(), [caption, table], Context);

        var tableChunk = Assert.Single(chunks, c => c.ChunkType == ChunkType.Table);
        Assert.Equal("Table: Revenue by segment was as follows:\nSegment | 2025\nCloud | 10.7", tableChunk.Text);
    }

    [Fact]
    public async Task Table_formatting_logs_one_summary_of_fallbacks_and_repacks()
    {
        var logger = new CapturingLogger();
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");

        CreateChunker(tableCells: true, logger: logger).Chunk(Guid.NewGuid(), [element, element], Context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("0 of 2 tables", entry.Message);
        Assert.Contains("0 table chunks exceeded the budget", entry.Message);
    }

    [Fact]
    public async Task With_table_switches_off_nothing_about_tables_is_logged()
    {
        var logger = new CapturingLogger();

        CreateChunker(logger: logger).Chunk(Guid.NewGuid(), [await TableFixtures.LoadAsync("jpm-financial-performance")], Context);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void An_unaligned_table_and_an_over_budget_row_are_counted_in_the_summary()
    {
        var logger = new CapturingLogger();
        var unaligned = new DocumentElement
        {
            Type = ElementType.Table,
            Text = "A | 1\nB | 2 | 3",
            Table = new TableGrid
            {
                Rows =
                [
                    new TableRow { Cells = [new TableCell { Text = "A" }, new TableCell { Text = "1", ColSpan = 2 }] },
                    new TableRow { Cells = [new TableCell { Text = "B" }, new TableCell { Text = "2" }, new TableCell { Text = "3" }] }
                ]
            }
        };

        CreateChunker(tableCells: true, logger: logger).Chunk(Guid.NewGuid(), [unaligned], Context);

        Assert.Contains("1 of 1 tables", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public async Task A_header_bigger_than_the_whole_budget_is_counted_as_a_repack()
    {
        var logger = new CapturingLogger();

        var chunks = CreateChunker(maxTokens: 30, overlapTokens: 0, tableCells: true, logger: logger)
            .Chunk(Guid.NewGuid(), [await TableFixtures.LoadAsync("jpm-financial-performance")], Context);

        Assert.DoesNotContain("; 0 table chunks exceeded", Assert.Single(logger.Entries).Message);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 30));
    }

    private const string TradingCaption = "Table: The following table provides information concerning trading arrangements:";

    private static List<DocumentElement> TradingArrangementsTable()
    {
        static TableRow Row(params string[] cells) => new() { Cells = cells.Select(c => new TableCell { Text = c }).ToList() };
        static TableRow Officer(string name) => Row($"{name}(b)", "Member of the Operating Committee and head of a business segment", "October 15, 2025",
            "Until December 31, 2026 or completion", "50% of the net issued shares of restricted stock units vesting in January, after tax withholding");

        var rows = new List<TableRow>
        {
            Row("Name", "Title of the officer at the time the arrangement was adopted", "Adoption date of the trading arrangement",
                "Duration of the trading arrangement(c)", "Aggregate number of shares of common stock to be sold under the arrangement(d)"),
            Officer("Ashley Bacon"),
            Officer("Jeremy Barnum"),
            Officer("Lori Beer"),
            Row("James Dimon(a)", "Chairman of the Board and Chief Executive Officer of the firm", "November 1, 2025", "Until November 1, 2026", "200,000")
        };
        rows.AddRange(new[] { "Mary Erdoes", "Teresa Heitsenrether", "Marianne Lake", "Douglas Petno", "Troy Rohrbaugh", "Daniel Pinto" }.Select(Officer));

        return
        [
            new DocumentElement { Type = ElementType.Paragraph, Text = "The following table provides information concerning trading arrangements:" },
            new DocumentElement { Type = ElementType.Table, Text = "x", Table = new TableGrid { Rows = rows } }
        ];
    }

    [Fact]
    public void A_header_block_bigger_than_half_the_budget_still_leaves_the_caption_on_every_table_chunk()
    {
        var chunks = CreateChunker(overlapTokens: 0, tableCells: true, tableHeaders: true, tableCaptions: true)
            .Chunk(Guid.NewGuid(), TradingArrangementsTable(), Context);

        var tableChunks = chunks.Where(c => c.ChunkType == ChunkType.Table).ToList();
        Assert.True(tableChunks.Count > 1);
        Assert.All(tableChunks, c => Assert.StartsWith(TradingCaption, c.Text));
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250, $"{c.TokenCount} tokens"));
        Assert.StartsWith(TradingCaption, Assert.Single(tableChunks, c => c.Text.Contains("200,000")).Text);
    }

    [Fact]
    public void A_header_block_bigger_than_half_the_budget_produces_no_over_budget_table_chunk()
    {
        var logger = new CapturingLogger();

        CreateChunker(overlapTokens: 0, tableCells: true, tableHeaders: true, tableCaptions: true, logger: logger)
            .Chunk(Guid.NewGuid(), TradingArrangementsTable(), Context);

        Assert.Contains("0 table chunks exceeded the budget", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public void A_body_row_longer_than_the_budget_minus_the_caption_is_split_under_the_caption()
    {
        const string captionText = "The following table summarizes the litigation reserves of the firm:";
        var longRow = "Litigation matters | " + string.Join(" ", Enumerable.Range(1, 9).Select(i => $"Matter number {i} remains pending before the court."));
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = captionText },
            new() { Type = ElementType.Table, Text = $"Item | 2025\nShort row | 1,000\n{longRow}\nAnother row | 2,000" }
        };
        var chunker = CreateChunker(maxTokens: 100, overlapTokens: 0, tableCaptions: true);

        var tableChunks = chunker.Chunk(Guid.NewGuid(), elements, Context).Where(c => c.ChunkType == ChunkType.Table).ToList();

        Assert.True(tableChunks.Count > 1);
        Assert.All(tableChunks, c => Assert.StartsWith("Table: " + captionText, c.Text));
        Assert.All(tableChunks, c => Assert.True(c.TokenCount <= 100, $"{c.TokenCount} tokens"));
        Assert.Contains(tableChunks, c => c.Text.Contains("Matter number 9 remains pending"));
    }

    private sealed class CapturingLogger : ILogger<Chunker>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
