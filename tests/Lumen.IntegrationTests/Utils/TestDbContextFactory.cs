namespace Lumen.IntegrationTests.Utils;

public static class TestDbContextFactory
{
    public static DbContextOptions<AppDbContext> BuildOptions(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return optionsBuilder.Options;
    }

    public static AppDbContext Create(string connectionString, Guid tenantId = default) =>
        new(BuildOptions(connectionString), new CurrentTenant { TenantId = tenantId });
}
