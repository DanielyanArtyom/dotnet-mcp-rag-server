namespace Lumen.Infrastructure.Tenancy;

public class CurrentTenant : ICurrentTenant
{
    public Guid TenantId { get; set; }
}
