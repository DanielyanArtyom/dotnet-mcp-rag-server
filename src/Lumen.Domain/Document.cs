namespace Lumen.Domain;

public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
}
