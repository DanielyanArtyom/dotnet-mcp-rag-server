namespace Lumen.Domain;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
}
