namespace Lumen.UnitTests.Chunking;

public class TableLayoutTests
{
    private static async Task<TableLayout> LayoutOf(string fixture) => TableLayout.Build((await TableFixtures.LoadAsync(fixture)).Table!);

    private static string[] Texts(LayoutRow row) => row.Cells.Select(c => c.Text).ToArray();

    [Fact]
    public async Task Merges_currency_and_percent_cells_into_their_numbers()
    {
        var layout = await LayoutOf("apple-operating-expenses");

        var rnd = layout.Rows.First(r => r.Cells[0].Text == "Research and development");
        Assert.Equal(["Research and development", "$34,550", "10%", "$31,370", "5%", "$29,915"], Texts(rnd));
    }

    [Fact]
    public async Task Finds_the_year_header_and_five_aligned_columns()
    {
        var layout = await LayoutOf("apple-operating-expenses");

        var header = Assert.Single(layout.Rows, r => r.Kind == RowKind.Header);
        Assert.Equal(["2025", "Change", "2024", "Change", "2023"], Texts(header));
        Assert.NotNull(layout.Columns);
        Assert.Equal(5, layout.Columns!.Count);
        Assert.Equal(["2025", "Change", "2024", "Change", "2023"], Enumerable.Range(0, 5).Select(layout.ColumnHeader));
    }

    [Fact]
    public async Task Keeps_empty_change_slots_in_position()
    {
        var layout = await LayoutOf("apple-operating-expenses");

        var share = layout.Rows.First(r => r.Cells[0].Text == "Percentage of total net sales");
        Assert.Equal([0, 2, 4], share.Cells.Skip(1).Select(layout.ColumnOf));
    }

    [Fact]
    public async Task Row_group_labels_before_the_first_data_row_are_not_header_rows()
    {
        var layout = await LayoutOf("apple-balance-sheet");

        Assert.Equal(["September 27, 2025", "September 28, 2024"], Texts(Assert.Single(layout.Rows, r => r.Kind == RowKind.Header)));
        Assert.Equal(["ASSETS:", "Current assets:", "Non-current assets:"], layout.Rows.Where(r => r.Kind == RowKind.GroupLabel).Select(r => r.Cells[0].Text));
    }

    [Fact]
    public async Task A_table_whose_first_row_is_a_group_label_has_no_header()
    {
        var layout = await LayoutOf("apple-gross-margin-percentage");

        Assert.False(layout.HasHeader);
        Assert.Equal(RowKind.GroupLabel, layout.Rows[0].Kind);
        Assert.NotNull(layout.Columns);
    }

    [Fact]
    public async Task Rowspan_cells_push_the_next_rows_to_the_right_and_footnote_cells_are_dropped()
    {
        var layout = await LayoutOf("jpm-selected-data");

        var revenue = layout.Rows.First(r => r.Cells[0].Text == "Total net revenue");
        Assert.Equal(["Total net revenue", "$57,347", "$49,836", "$45,798", "$46,427", "$44,912", "$107,183", "$90,222"], Texts(revenue));
        Assert.Equal("2Q26", layout.ColumnHeader(0));
        Assert.Equal("Six months ended June 30, 2026", layout.ColumnHeader(5));
    }

    [Fact]
    public async Task A_two_level_header_labels_each_column_top_down()
    {
        var layout = await LayoutOf("jpm-capital-ratios");

        Assert.Equal("Standardized, JPMorgan Chase & Co.", layout.ColumnHeader(0));
        Assert.Equal("Advanced, JPMorgan Chase Bank, N.A.", layout.ColumnHeader(3));
    }

    [Fact]
    public async Task A_title_row_stays_in_the_header_block_but_does_not_label_columns()
    {
        var layout = await LayoutOf("jpm-revenue");

        Assert.Equal("Revenue", layout.Rows[0].Cells[0].Text);
        Assert.Equal(RowKind.Header, layout.Rows[0].Kind);
        Assert.Equal("Three months ended June 30, 2026", layout.ColumnHeader(0));
        Assert.Equal("Change", layout.ColumnHeader(2));
    }

    [Fact]
    public async Task A_table_without_data_rows_has_no_header_and_no_columns()
    {
        var layout = await LayoutOf("jpm-trading-arrangements");

        Assert.False(layout.HasData);
        Assert.False(layout.HasHeader);
        Assert.Null(layout.Columns);
    }

    [Fact]
    public void Two_values_of_one_row_in_the_same_column_make_the_table_unaligned()
    {
        var grid = new TableGrid
        {
            Rows =
            [
                new TableRow { Cells = [new TableCell { Text = "A" }, new TableCell { Text = "1", ColSpan = 2 }] },
                new TableRow { Cells = [new TableCell { Text = "B" }, new TableCell { Text = "2" }, new TableCell { Text = "3" }] }
            ]
        };

        Assert.Null(TableLayout.Build(grid).Columns);
    }

    [Fact]
    public void A_rowspan_past_the_last_row_and_ragged_rows_do_not_throw()
    {
        var grid = new TableGrid
        {
            Rows =
            [
                new TableRow { Cells = [new TableCell { Text = "A", RowSpan = 100, ColSpan = 100 }, new TableCell { Text = "1" }] },
                new TableRow { Cells = [new TableCell { Text = "2" }] }
            ]
        };

        var layout = TableLayout.Build(grid);

        Assert.Equal(2, layout.Rows.Count);
        Assert.Equal(100, layout.Rows[1].Cells[0].Start);
    }

    [Fact]
    public void A_year_label_is_a_data_row_label_not_a_header()
    {
        var grid = new TableGrid
        {
            Rows =
            [
                new TableRow { Cells = [new TableCell { Text = "2026" }, new TableCell { Text = "$4,752" }] },
                new TableRow { Cells = [new TableCell { Text = "2027" }, new TableCell { Text = "3,708" }] }
            ]
        };

        Assert.All(TableLayout.Build(grid).Rows, r => Assert.Equal(RowKind.Data, r.Kind));
    }

    [Fact]
    public async Task The_Apple_10K_tables_align_and_find_headers_at_the_measured_rates()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"));
        var tables = (await new HtmlDocumentReader().ReadAsync(stream)).Where(e => e.Table is not null).Select(e => TableLayout.Build(e.Table!)).ToList();

        var withData = tables.Where(t => t.HasData).ToList();
        var aligned = withData.Where(t => t.Columns is not null).ToList();
        Assert.True(aligned.Count >= withData.Count * 0.95, $"aligned {aligned.Count} of {withData.Count}");
        Assert.True(aligned.Count(t => t.HasHeader) >= aligned.Count * 0.85, $"with header {aligned.Count(t => t.HasHeader)} of {aligned.Count}");
    }

    private static TableRow Row(params string[] cells) => new() { Cells = cells.Select(c => new TableCell { Text = c }).ToList() };

    [Fact]
    public void A_dollar_dash_is_a_value()
    {
        var grid = new TableGrid
        {
            Rows =
            [
                Row("", "", "2025", "", "2024"),
                Row("Line item", "$", "—", "$", "1,234"),
                Row("Other assets", "$", "—")
            ]
        };

        var layout = TableLayout.Build(grid);

        Assert.Equal(RowKind.Data, layout.Rows[1].Kind);
        Assert.Equal(["Line item", "$—", "$1,234"], Texts(layout.Rows[1]));
        Assert.Equal(RowKind.Data, layout.Rows[2].Kind);
        Assert.Equal(["Other assets", "$—"], Texts(layout.Rows[2]));
    }
}
