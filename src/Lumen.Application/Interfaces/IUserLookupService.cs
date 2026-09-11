namespace Lumen.Application.Interfaces;

public interface IUserLookupService
{
    Task<User?> FindByEmailAcrossTenantsAsync(string email, CancellationToken ct = default);
}
