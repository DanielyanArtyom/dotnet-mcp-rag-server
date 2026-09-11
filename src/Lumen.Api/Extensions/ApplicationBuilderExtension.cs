namespace Lumen.Api.Extensions;

public static class ApplicationBuilderExtension
{
    public static async Task UseDatabaseMigrationAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var seedTenant = scope.ServiceProvider.GetRequiredService<CurrentTenant>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        await DbSeeder.SeedAsync(db, seedTenant, hasher);
    }

    public static IApplicationBuilder UseMiddlewares(this IApplicationBuilder app)
    {
        app.UseMiddleware<TenantResolutionMiddleware>();

        return app;
    }
}
