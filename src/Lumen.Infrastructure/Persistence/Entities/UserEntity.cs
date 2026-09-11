namespace Lumen.Infrastructure.Persistence.Entities;

public class UserEntity : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public Role Role { get; set; }
}
