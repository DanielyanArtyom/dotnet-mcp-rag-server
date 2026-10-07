namespace Lumen.Domain.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string RawFilePath { get; set; } = default!;
    public DateOnly FilingDate { get; set; }
    public string? IngestProfile { get; set; }
}
