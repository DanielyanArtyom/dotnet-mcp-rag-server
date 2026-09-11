using Lumen.Infrastructure.Auth;
using Lumen.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Lumen.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLumenServices(this IServiceCollection services)
    {
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());

        services.AddScoped<IRepository<Document>, DocumentRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
