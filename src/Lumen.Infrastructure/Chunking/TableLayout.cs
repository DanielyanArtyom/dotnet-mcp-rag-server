namespace Lumen.Infrastructure.Chunking;

public class TableLayout
{
    public const int MaxHeaderRows = 6;

    private static readonly Regex Numeric = new(@"^[-−–]?\$?\(?\$?\d[\d,]*(?:\.\d+)?%?\)?%?$|^\$?(?:—|–|-|NM|N/A)$", RegexOptions.Compiled);
    private static readonly Regex Year = new(@"^(?:19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex FootnoteMarker = new(@"^\([a-z]{1,2}\)$", RegexOptions.Compiled);
    private static readonly HashSet<string> TrailingSymbols = ["%", ")", ")%", "%)"];

    private TableLayout(List<LayoutRow> rows, List<LayoutColumn>? columns, int labelEnd)
    {
        Rows = rows;
        Columns = columns;
        LabelEnd = labelEnd;
    }

    public List<LayoutRow> Rows { get; }

    public List<LayoutColumn>? Columns { get; }

    public int LabelEnd { get; }

    public bool HasHeader => Rows.Any(r => r.Kind == RowKind.Header);

    public bool HasData => Rows.Any(r => r.Kind == RowKind.Data);

    public static TableLayout Build(TableGrid grid)
    {
        var rows = Place(grid)
            .Select(MergeSymbols)
            .Where(cells => cells.Count > 0)
            .Select(cells => new LayoutRow { Cells = cells })
            .ToList();
        Classify(rows);
        var columns = AlignColumns(rows, out var labelEnd);
        return new TableLayout(rows, columns, labelEnd);
    }

    public int ColumnOf(LayoutCell cell) => Columns!.FindIndex(c => cell.Start < c.End && cell.End > c.Start);

    public string ColumnHeader(int column)
    {
        var target = Columns![column];
        var parts = Rows
            .Where(r => r.Kind == RowKind.Header && !IsTitle(r))
            .SelectMany(r => r.Cells)
            .Where(c => c.Start >= LabelEnd && c.Start < target.End && c.End > target.Start)
            .Select(c => c.Text.TrimEnd(',', ':', ' '));
        return string.Join(", ", parts);
    }

    private bool IsTitle(LayoutRow row) => row.Cells.Count == 1 && row.Cells[0].Start < LabelEnd;

    private static List<List<LayoutCell>> Place(TableGrid grid)
    {
        var occupied = new HashSet<(int Row, int Column)>();
        var placed = new List<List<LayoutCell>>();
        for (var r = 0; r < grid.Rows.Count; r++)
        {
            var column = 0;
            var cells = new List<LayoutCell>();
            foreach (var cell in grid.Rows[r].Cells)
            {
                while (occupied.Contains((r, column)))
                {
                    column++;
                }

                for (var below = r + 1; below < Math.Min(r + cell.RowSpan, grid.Rows.Count); below++)
                {
                    for (var c = column; c < column + cell.ColSpan; c++)
                    {
                        occupied.Add((below, c));
                    }
                }

                cells.Add(new LayoutCell { Text = cell.Text, Start = column, End = column + cell.ColSpan });
                column += cell.ColSpan;
            }

            placed.Add(cells);
        }

        return placed;
    }

    // SEC filings put "$", "%" and ")" in cells of their own and footnote markers like "(e)" between columns.
    private static List<LayoutCell> MergeSymbols(List<LayoutCell> cells)
    {
        var merged = new List<LayoutCell>();
        foreach (var cell in cells.Where(c => c.Text.Length > 0))
        {
            if (merged.Count > 0 && TrailingSymbols.Contains(cell.Text))
            {
                merged[^1].Text += cell.Text;
                merged[^1].End = cell.End;
            }
            else if (merged.Count > 0 && FootnoteMarker.IsMatch(cell.Text))
            {
            }
            else if (merged.Count > 0 && merged[^1].Text == "$")
            {
                merged[^1].Text = "$" + cell.Text;
                merged[^1].End = cell.End;
            }
            else
            {
                merged.Add(new LayoutCell { Text = cell.Text, Start = cell.Start, End = cell.End });
            }
        }

        return merged;
    }

    private static bool IsValue(string text) => Numeric.IsMatch(text) && !Year.IsMatch(text);

    private static bool IsLabel(string text) => !Numeric.IsMatch(text) || Year.IsMatch(text);

    private static bool IsData(LayoutRow row) =>
        row.Cells.Count >= 2 && IsLabel(row.Cells[0].Text) && row.Cells.Skip(1).Any(c => IsValue(c.Text));

    private static bool IsGroupLabel(LayoutRow row) => row.Cells.Count == 1 && !IsValue(row.Cells[0].Text);

    private static void Classify(List<LayoutRow> rows)
    {
        var firstData = rows.FindIndex(IsData);
        var headerEnd = 0;
        if (firstData > 0)
        {
            headerEnd = firstData;
            var labelEnd = rows[firstData].Cells[0].End;
            while (headerEnd > 0 && rows[headerEnd - 1].Cells.Count == 1 && rows[headerEnd - 1].Cells[0].Start < labelEnd)
            {
                headerEnd--;
            }

            if (headerEnd > MaxHeaderRows)
            {
                headerEnd = 0;
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Kind = i < headerEnd ? RowKind.Header
                : IsData(rows[i]) ? RowKind.Data
                : IsGroupLabel(rows[i]) ? RowKind.GroupLabel
                : RowKind.Other;
        }
    }

    private static List<LayoutColumn>? AlignColumns(List<LayoutRow> rows, out int labelEnd)
    {
        var data = rows.Where(r => r.Kind == RowKind.Data).ToList();
        labelEnd = data.Count == 0 ? 0 : data.Max(r => r.Cells[0].End);
        if (data.Count == 0 || data.Select(r => r.Cells[0].Start).Distinct().Count() != 1)
        {
            return null;
        }

        var columns = new List<LayoutColumn>();
        foreach (var (start, end) in data.SelectMany(r => r.Cells.Skip(1)).Select(c => (c.Start, c.End)).Distinct().OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            if (columns.Count > 0 && start < columns[^1].End)
            {
                columns[^1] = columns[^1] with { End = Math.Max(columns[^1].End, end) };
            }
            else
            {
                columns.Add(new LayoutColumn(start, end));
            }
        }

        if (columns[0].Start < labelEnd)
        {
            return null;
        }

        foreach (var row in data)
        {
            var used = new HashSet<int>();
            foreach (var cell in row.Cells.Skip(1))
            {
                if (!used.Add(columns.FindIndex(c => cell.Start < c.End && cell.End > c.Start)))
                {
                    return null;
                }
            }
        }

        return columns;
    }
}
