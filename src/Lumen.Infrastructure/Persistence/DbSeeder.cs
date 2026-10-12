namespace Lumen.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context, CurrentTenant currentTenant, IPasswordHasher<User> passwordHasher)
    {
        // Scoped to this seeder's own known tenant, not "any tenant at all" — a broader
        // guard would wrongly skip seeding if the database already has unrelated tenants.
        if (await context.Tenants.AnyAsync(t => t.Name == "Acme Corp"))
        {
            return;
        }

        var tenantA = new TenantEntity { Id = Guid.NewGuid(), Name = "Acme Corp" };
        var tenantB = new TenantEntity { Id = Guid.NewGuid(), Name = "Globex Inc" };
        context.Tenants.AddRange(tenantA, tenantB);
        await context.SaveChangesAsync();

        // Sequential, not Task.WhenAll: both calls share the same AppDbContext (not thread-safe
        // for concurrent operations) and the same mutable CurrentTenant that TenantScopedWriter
        // flips for the duration of each write — running them in parallel would race on both.
        await SeedTenantAsync(context, currentTenant, passwordHasher, tenantA.Id, "admin@acme.test");
        await SeedTenantAsync(context, currentTenant, passwordHasher, tenantB.Id, "admin@globex.test");
    }

    private static Task SeedTenantAsync(
        AppDbContext context,
        CurrentTenant currentTenant,
        IPasswordHasher<User> passwordHasher,
        Guid tenantId,
        string adminEmail)
    {
        return TenantScopedWriter.RunAsync(currentTenant, tenantId, async () =>
        {
            var user = new UserEntity { Id = Guid.NewGuid(), Email = adminEmail, Role = Role.Admin };
            user.PasswordHash = passwordHasher.HashPassword(
                new User { Id = user.Id, TenantId = tenantId, Email = adminEmail, Role = Role.Admin }, "Passw0rd!");
            context.Users.Add(user);

            await context.SaveChangesAsync();
        });
    }
}
