namespace Lumen.Application.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, Guid tenantId, Role role);
}
