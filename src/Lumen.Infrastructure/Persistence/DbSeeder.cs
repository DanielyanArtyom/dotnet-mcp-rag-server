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
        await SeedTenantAsync(context, currentTenant, passwordHasher, tenantA.Id, "admin@acme.test", "Welcome to Acme", "This is Acme's first document.");
        await SeedTenantAsync(context, currentTenant, passwordHasher, tenantB.Id, "admin@globex.test", "Welcome to Globex", "This is Globex's first document.");
    }

    private static Task SeedTenantAsync(
        AppDbContext context,
        CurrentTenant currentTenant,
        IPasswordHasher<User> passwordHasher,
        Guid tenantId,
        string adminEmail,
        string documentTitle,
        string documentContent)
    {
        return TenantScopedWriter.RunAsync(currentTenant, tenantId, async () =>
        {
            var user = new UserEntity { Id = Guid.NewGuid(), Email = adminEmail, Role = Role.Admin };
            user.PasswordHash = passwordHasher.HashPassword(
                new User { Id = user.Id, TenantId = tenantId, Email = adminEmail, Role = Role.Admin }, "Passw0rd!");
            context.Users.Add(user);

            var documentId = Guid.NewGuid();
            context.Documents.Add(new DocumentEntity
            {
                Id = documentId,
                Title = documentTitle,
                Content = documentContent,
                RawFilePath = $"{tenantId}/{documentId}.htm",
                FilingDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });

            await context.SaveChangesAsync();
        });
    }
}
