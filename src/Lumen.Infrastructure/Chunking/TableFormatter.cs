namespace Lumen.Infrastructure.Chunking;

public static class TableFormatter
{
    public static FormattedTable Format(DocumentElement table, string? caption, ChunkingOptions options)
    {
        if (!options.TableCells || table.Table is null)
        {
            return new FormattedTable { Caption = caption, BodyLines = table.Text.Split('\n').Select(line => new TableLine(line, false)).ToList() };
        }

        var layout = TableLayout.Build(table.Table);
        var keyValue = options.TableFormat == TableFormat.KeyValue && layout.Columns is not null && layout.HasHeader;
        var headerRows = layout.Rows.Where(r => r.Kind == RowKind.Header);
        var headerLines = keyValue
            ? headerRows.Select(r => string.Join(" | ", r.Cells.Where(c => c.Start < layout.LabelEnd).Select(c => c.Text))).Where(line => line.Length > 0).ToList()
            : headerRows.Select(Compact).ToList();
        var body = new List<TableLine>();
        string? groupLabel = null;
        var groupUsed = false;
        var groupPosition = 0;

        void KeepUnusedGroup()
        {
            if (groupLabel is not null && !groupUsed)
            {
                body.Insert(groupPosition, new TableLine(groupLabel, false));
            }
        }

        foreach (var row in layout.Rows.Where(r => r.Kind != RowKind.Header))
        {
            switch (row.Kind)
            {
                case RowKind.GroupLabel when keyValue:
                    KeepUnusedGroup();
                    groupLabel = row.Cells[0].Text;
                    groupUsed = false;
                    groupPosition = body.Count;
                    break;
                case RowKind.GroupLabel:
                    body.Add(new TableLine(Compact(row), true));
                    break;
                case RowKind.Data when keyValue:
                    body.Add(new TableLine(KeyValueLine(row, layout, groupLabel?.TrimEnd(':', ' ')), false));
                    groupUsed = true;
                    break;
                case RowKind.Data when layout.Columns is not null:
                    body.Add(new TableLine(AlignedLine(row, layout), false));
                    break;
                default:
                    body.Add(new TableLine(Compact(row), false));
                    break;
            }
        }

        KeepUnusedGroup();

        return new FormattedTable
        {
            Caption = caption,
            HeaderLines = headerLines,
            BodyLines = body,
            RepeatHeader = options.TableHeaders,
            HasData = layout.HasData,
            ColumnsFellBack = layout.HasData && layout.Columns is null
        };
    }

    private static string Compact(LayoutRow row) => string.Join(" | ", row.Cells.Select(c => c.Text));

    // CleanText has already collapsed whitespace inside cells, so a double space can only come from an empty slot.
    private static string AlignedLine(LayoutRow row, TableLayout layout)
    {
        var slots = Enumerable.Repeat(string.Empty, layout.Columns!.Count).ToArray();
        foreach (var cell in row.Cells.Skip(1))
        {
            slots[layout.ColumnOf(cell)] = cell.Text;
        }

        var last = Array.FindLastIndex(slots, s => s.Length > 0);
        return string.Join(" | ", slots.Take(last + 1).Prepend(row.Cells[0].Text)).Replace("  ", " ");
    }

    private static string KeyValueLine(LayoutRow row, TableLayout layout, string? group)
    {
        var label = group is null ? row.Cells[0].Text : $"{group} › {row.Cells[0].Text}";
        var values = row.Cells.Skip(1).Select(cell =>
        {
            var header = layout.ColumnHeader(layout.ColumnOf(cell));
            return header.Length == 0 ? cell.Text : $"{header}: {cell.Text}";
        });
        return string.Join(" | ", values.Prepend(label));
    }
}
