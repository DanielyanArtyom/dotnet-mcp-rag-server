namespace Lumen.UnitTests.Chunking;

public class TableFormatterTests
{
    private static readonly ChunkingOptions Cells = new() { TableCells = true };
    private static readonly ChunkingOptions CellsAndHeaders = new() { TableCells = true, TableHeaders = true };
    private static readonly ChunkingOptions KeyValue = new() { TableCells = true, TableHeaders = true, TableFormat = TableFormat.KeyValue };

    private static string[] Body(FormattedTable table) => table.BodyLines.Select(l => l.Text).ToArray();

    [Fact]
    public async Task Without_TableCells_the_body_is_todays_text()
    {
        var element = await TableFixtures.LoadAsync("apple-operating-expenses");

        var table = TableFormatter.Format(element, caption: "Operating expenses were as follows:", new ChunkingOptions { TableCaptions = true });

        Assert.Equal(element.Text.Split('\n'), Body(table));
        Assert.Empty(table.HeaderLines);
        Assert.Equal("Operating expenses were as follows:", table.Caption);
        Assert.False(table.RepeatHeader);
    }

    [Fact]
    public async Task Pipe_rows_keep_empty_slots_and_merged_symbols()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("apple-operating-expenses"), null, Cells);

        Assert.Equal(["2025 | Change | 2024 | Change | 2023"], table.HeaderLines);
        Assert.Equal("Research and development | $34,550 | 10% | $31,370 | 5% | $29,915", Body(table)[0]);
        Assert.Equal("Percentage of total net sales | 8% | | 8% | | 8%", Body(table)[1]);
        Assert.False(table.RepeatHeader);
    }

    [Fact]
    public async Task Group_labels_are_marked_as_such()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-financial-performance"), null, CellsAndHeaders);

        Assert.Contains(table.BodyLines, l => l is { Text: "Capital ratios - Standardized(a)", IsGroupLabel: true });
        Assert.Contains("CET1 capital | 14.2% | 15.1% | | 14.2% | 15.1%", Body(table));
        Assert.Equal(3, table.HeaderLines.Count);
        Assert.True(table.RepeatHeader);
    }

    [Fact]
    public async Task Key_value_rows_carry_their_column_headers_and_group()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-capital-ratios"), null, KeyValue);

        Assert.Equal(["June 30, 2026 (in millions, except ratios)"], table.HeaderLines);
        Assert.True(table.RepeatHeader);
        Assert.Contains(
            "Risk-based capital metrics › CET1 capital ratio | Standardized, JPMorgan Chase & Co.: 14.2% | Standardized, JPMorgan Chase Bank, N.A.: 15.0% | Advanced, JPMorgan Chase & Co.: 14.2% | Advanced, JPMorgan Chase Bank, N.A.: 16.1%",
            Body(table));
        Assert.DoesNotContain(table.BodyLines, l => l.IsGroupLabel);
    }

    [Fact]
    public async Task Key_value_keeps_title_and_unit_text_from_the_label_area_of_the_header()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-financial-performance"), null, KeyValue);

        Assert.Equal(
            ["Financial performance of JPMorganChase", "(unaudited) As of or for the period ended, (in millions, except per share data and ratios)"],
            table.HeaderLines);
        Assert.True(table.RepeatHeader);
    }

    [Fact]
    public async Task Key_value_without_TableHeaders_does_not_repeat_the_header_lines()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-financial-performance"), null, new ChunkingOptions { TableCells = true, TableFormat = TableFormat.KeyValue });

        Assert.False(table.RepeatHeader);
    }

    [Fact]
    public async Task Key_value_keeps_a_group_label_that_prefixes_no_data_row()
    {
        var body = Body(TableFormatter.Format(await TableFixtures.LoadAsync("apple-balance-sheet"), null, KeyValue)).ToList();

        var assets = body.IndexOf("ASSETS:");
        Assert.True(assets >= 0);
        Assert.True(assets < body.FindIndex(l => l.StartsWith("Current assets › ")));
    }

    [Fact]
    public void Key_value_keeps_a_trailing_single_cell_note()
    {
        var element = new DocumentElement
        {
            Type = ElementType.Table,
            Text = "2025 | 2024\nRevenue | 10 | 9\nCosts | 4 | 3\n(a) Unaudited.",
            Table = new TableGrid
            {
                Rows =
                [
                    new TableRow { Cells = [new TableCell { Text = "" }, new TableCell { Text = "2025" }, new TableCell { Text = "2024" }] },
                    new TableRow { Cells = [new TableCell { Text = "Revenue" }, new TableCell { Text = "10" }, new TableCell { Text = "9" }] },
                    new TableRow { Cells = [new TableCell { Text = "Costs" }, new TableCell { Text = "4" }, new TableCell { Text = "3" }] },
                    new TableRow { Cells = [new TableCell { Text = "Amounts are unaudited.", ColSpan = 3 }] }
                ]
            }
        };

        var table = TableFormatter.Format(element, null, KeyValue);

        Assert.Equal(["Revenue | 2025: 10 | 2024: 9", "Costs | 2025: 4 | 2024: 3", "Amounts are unaudited."], Body(table));
        Assert.DoesNotContain(table.BodyLines, l => l.IsGroupLabel);
    }

    [Fact]
    public async Task Key_value_skips_empty_slots()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("apple-operating-expenses"), null, KeyValue);

        Assert.Equal("Research and development | 2025: $34,550 | Change: 10% | 2024: $31,370 | Change: 5% | 2023: $29,915", Body(table)[0]);
        Assert.Equal("Percentage of total net sales | 2025: 8% | 2024: 8% | 2023: 8%", Body(table)[1]);
    }

    [Fact]
    public async Task Key_value_falls_back_to_pipe_for_a_table_without_a_header()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("apple-gross-margin-percentage"), null, KeyValue);

        Assert.Equal(["Gross margin percentage:", "Products | 36.8% | 37.2% | 36.5%", "Services | 75.4% | 73.9% | 70.8%", "Total gross margin percentage | 46.9% | 46.2% | 44.1%"], Body(table));
    }

    [Fact]
    public async Task A_table_without_data_rows_renders_compact_rows_and_is_not_a_fallback()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-trading-arrangements"), null, KeyValue);

        Assert.Equal("Name | Title | Adoption date | Duration(c) | Aggregate number of shares to be sold(d)", Body(table)[0]);
        Assert.Empty(table.HeaderLines);
        Assert.False(table.ColumnsFellBack);
    }

    [Fact]
    public void An_unaligned_table_renders_compact_rows_and_reports_the_fallback()
    {
        var element = new DocumentElement
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

        var table = TableFormatter.Format(element, null, Cells);

        Assert.True(table.ColumnsFellBack);
        Assert.Equal(["A | 1", "B | 2 | 3"], Body(table));
    }

    [Fact]
    public void A_table_without_a_grid_uses_todays_text_even_with_every_switch_on()
    {
        var element = new DocumentElement { Type = ElementType.Table, Text = "Revenue | 10.5 | 9.8" };

        var table = TableFormatter.Format(element, "caption:", KeyValue);

        Assert.Equal(["Revenue | 10.5 | 9.8"], Body(table));
        Assert.Equal("caption:", table.Caption);
        Assert.False(table.RepeatHeader);
    }
}
