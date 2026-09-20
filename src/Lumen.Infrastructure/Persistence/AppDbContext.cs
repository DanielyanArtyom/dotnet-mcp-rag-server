namespace Lumen.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<UserEntity>().HasQueryFilter(u => u.TenantId == _currentTenant.TenantId);
        modelBuilder.Entity<DocumentEntity>().HasQueryFilter(d => d.TenantId == _currentTenant.TenantId);
        modelBuilder.Entity<DocumentChunkEntity>().HasQueryFilter(c => c.TenantId == _currentTenant.TenantId);
    }

    // Every SaveChanges overload funnels through here and always overwrites TenantId with the
    // ambient tenant — a caller can never win by setting it itself. See Tenancy.TenantScopedWriter
    // for writing rows on behalf of a specific tenant outside the normal per-request flow.
    private void StampTenantId()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>()
                     .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = _currentTenant.TenantId;
        }
    }

    public override int SaveChanges()
    {
        StampTenantId();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenantId();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantId();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenantId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
