namespace Lumen.Application.Ingestion;

public class DocumentElement
{
    public ElementType Type { get; set; }
    public string Text { get; set; } = default!;
    public TableGrid? Table { get; set; }
}
