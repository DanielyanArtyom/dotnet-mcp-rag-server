namespace Lumen.Infrastructure.Persistence;

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
