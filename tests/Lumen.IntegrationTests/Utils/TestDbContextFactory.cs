using Pgvector.EntityFrameworkCore;

namespace Lumen.IntegrationTests.Utils;

public static class TestDbContextFactory
{
    public static DbContextOptions<AppDbContext> BuildOptions(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());
        return optionsBuilder.Options;
    }

    public static AppDbContext Create(string connectionString, Guid tenantId = default) =>
        new(BuildOptions(connectionString), new CurrentTenant { TenantId = tenantId });
}
