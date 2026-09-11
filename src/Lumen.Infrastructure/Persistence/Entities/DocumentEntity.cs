namespace Lumen.Infrastructure.Persistence.Entities;

public class DocumentEntity : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
}
