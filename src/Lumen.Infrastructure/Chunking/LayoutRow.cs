namespace Lumen.Infrastructure.Chunking;

public class LayoutRow
{
    public List<LayoutCell> Cells { get; set; } = [];
    public RowKind Kind { get; set; }
}
