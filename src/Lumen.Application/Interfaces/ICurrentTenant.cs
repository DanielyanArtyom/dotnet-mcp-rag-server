namespace Lumen.Application.Interfaces;

public interface ICurrentTenant
{
    Guid TenantId { get; }
}
