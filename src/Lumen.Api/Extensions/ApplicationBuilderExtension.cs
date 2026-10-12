namespace Lumen.Api.Extensions;

public static class ApplicationBuilderExtension
{
    public static async Task UseStartupDataAsync(this WebApplication app)
    {
        var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (database.MigrateOnStartup)
        {
            await db.Database.MigrateAsync();
        }

        if (database.SeedDemoData)
        {
            var seedTenant = scope.ServiceProvider.GetRequiredService<CurrentTenant>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            await DbSeeder.SeedAsync(db, seedTenant, hasher);
        }

        var demo = app.Services.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(demo.FilingsPath))
        {
            var folder = Path.IsPathRooted(demo.FilingsPath) ? demo.FilingsPath : Path.Combine(app.Environment.ContentRootPath, demo.FilingsPath);
            await scope.ServiceProvider.GetRequiredService<DemoFilingSeeder>().SeedAsync(folder, CancellationToken.None);
        }
    }

    public static IApplicationBuilder UseMiddlewares(this IApplicationBuilder app)
    {
        app.UseMiddleware<McpOriginMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();

        return app;
    }
}
