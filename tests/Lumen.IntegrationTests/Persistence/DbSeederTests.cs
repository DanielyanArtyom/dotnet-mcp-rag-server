namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class DbSeederTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SeedAsync_creates_two_tenants_with_one_admin_each_and_no_documents_and_is_idempotent()
    {
        var options = TestDbContextFactory.BuildOptions(fixture.ConnectionString);
        var currentTenant = new CurrentTenant();
        await using var context = new AppDbContext(options, currentTenant);
        var hasher = new PasswordHasher<User>();

        await DbSeeder.SeedAsync(context, currentTenant, hasher);
        await DbSeeder.SeedAsync(context, currentTenant, hasher); // idempotency check — second call must not duplicate rows

        // Scoped to known rows by name/email, not raw table counts — the Postgres
        // container is shared across the whole test collection, so a raw count would
        // be order-dependent and flaky.
        await using var verify = new AppDbContext(options, new CurrentTenant());
        Assert.Equal(1, await verify.Tenants.CountAsync(t => t.Name == "Acme Corp"));
        Assert.Equal(1, await verify.Tenants.CountAsync(t => t.Name == "Globex Inc"));
        Assert.Equal(1, await verify.Users.IgnoreQueryFilters().CountAsync(u => u.Email == "admin@acme.test"));
        Assert.Equal(1, await verify.Users.IgnoreQueryFilters().CountAsync(u => u.Email == "admin@globex.test"));
        var tenantIds = await verify.Tenants.Where(t => t.Name == "Acme Corp" || t.Name == "Globex Inc").Select(t => t.Id).ToListAsync();
        Assert.Equal(0, await verify.Documents.IgnoreQueryFilters().CountAsync(d => tenantIds.Contains(d.TenantId)));
    }
}
