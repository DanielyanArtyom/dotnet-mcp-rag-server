namespace Lumen.Application.Interfaces;

public interface ITenantLookupService
{
    Task<string?> GetNameAsync(Guid tenantId, CancellationToken ct = default);
}
