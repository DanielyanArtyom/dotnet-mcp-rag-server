namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class TenantIsolationDbContextTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Tenant_A_never_sees_Tenant_Bs_documents()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using (var seedContext = TestDbContextFactory.Create(fixture.ConnectionString, tenantAId))
        {
            seedContext.Tenants.Add(new TenantEntity { Id = tenantAId, Name = "Tenant A" });
            seedContext.Documents.Add(new DocumentEntity { Id = Guid.NewGuid(), TenantId = tenantAId, Title = "A doc", Content = "belongs to A", RawFilePath = "a.htm", FilingDate = new DateOnly(2025, 1, 1) });
            await seedContext.SaveChangesAsync();
        }

        await using (var seedContext = TestDbContextFactory.Create(fixture.ConnectionString, tenantBId))
        {
            seedContext.Tenants.Add(new TenantEntity { Id = tenantBId, Name = "Tenant B" });
            seedContext.Documents.Add(new DocumentEntity { Id = Guid.NewGuid(), TenantId = tenantBId, Title = "B doc", Content = "belongs to B", RawFilePath = "b.htm", FilingDate = new DateOnly(2025, 1, 1) });
            await seedContext.SaveChangesAsync();
        }

        await using var contextAsTenantA = TestDbContextFactory.Create(fixture.ConnectionString, tenantAId);
        var visibleToA = await contextAsTenantA.Documents.ToListAsync();

        Assert.Single(visibleToA);
        Assert.All(visibleToA, d => Assert.Equal(tenantAId, d.TenantId));
    }
}
