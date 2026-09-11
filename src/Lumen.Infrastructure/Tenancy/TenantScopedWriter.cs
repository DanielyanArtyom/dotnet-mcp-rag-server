namespace Lumen.Infrastructure.Tenancy;

// AppDbContext.SaveChanges always stamps new tenant-owned rows from the ambient ICurrentTenant —
// no per-call override. Code writing rows for a specific tenant outside the normal request flow
// (e.g. seed data) points CurrentTenant at that tenant for the write via this helper instead.
public static class TenantScopedWriter
{
    public static async Task RunAsync(CurrentTenant currentTenant, Guid tenantId, Func<Task> writeAndSave)
    {
        var previousTenantId = currentTenant.TenantId;
        currentTenant.TenantId = tenantId;
        try
        {
            await writeAndSave();
        }
        finally
        {
            currentTenant.TenantId = previousTenantId;
        }
    }
}
