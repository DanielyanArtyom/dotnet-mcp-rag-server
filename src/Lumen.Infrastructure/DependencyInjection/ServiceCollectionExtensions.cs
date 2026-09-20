namespace Lumen.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLumenServices(this IServiceCollection services)
    {
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());

        services.AddScoped<IRepository<Document>, DocumentRepository>();
        services.AddScoped<IDocumentChunkRepository, DocumentChunkRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddScoped<ITenantLookupService, TenantLookupService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IDocumentReaderFactory, DocumentReaderFactory>();
        services.AddScoped<IChunker, Chunker>();
        services.AddScoped<IFileStorage, LocalFileStorage>();

        return services;
    }
}
