namespace Lumen.Infrastructure.Tenancy;

public class TenantLookupService : ITenantLookupService
{
    private readonly AppDbContext _context;

    public TenantLookupService(AppDbContext context)
    {
        _context = context;
    }

    public Task<string?> GetNameAsync(Guid tenantId, CancellationToken ct = default)
    {
        return _context.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => (string?)t.Name)
            .FirstOrDefaultAsync(ct);
    }
}
