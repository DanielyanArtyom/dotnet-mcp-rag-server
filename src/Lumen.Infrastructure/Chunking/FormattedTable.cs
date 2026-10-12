namespace Lumen.Infrastructure.Chunking;

public class FormattedTable
{
    public string? Caption { get; init; }
    public List<string> HeaderLines { get; init; } = [];
    public List<TableLine> BodyLines { get; init; } = [];
    public bool RepeatHeader { get; init; }
    public bool HasData { get; init; }
    public bool ColumnsFellBack { get; init; }
}
