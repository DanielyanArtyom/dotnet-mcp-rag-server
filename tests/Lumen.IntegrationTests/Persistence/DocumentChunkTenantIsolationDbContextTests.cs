namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class DocumentChunkTenantIsolationDbContextTests(PostgresFixture fixture)
{
    private async Task SeedTenantWithChunkAsync(Guid tenantId, string name, string chunkText)
    {
        await using var context = TestDbContextFactory.Create(fixture.ConnectionString, tenantId);
        context.Tenants.Add(new TenantEntity { Id = tenantId, Name = name });
        var document = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = $"{name} doc",
            Content = "content",
            RawFilePath = $"{tenantId}/doc.htm",
            FilingDate = new DateOnly(2025, 1, 1)
        };
        context.Documents.Add(document);
        context.DocumentChunks.Add(new DocumentChunkEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentId = document.Id,
            ChunkIndex = 0,
            Text = chunkText,
            TokenCount = 3,
            CharStart = 0,
            CharEnd = chunkText.Length,
            ChunkType = ChunkType.Prose
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Tenant_A_never_sees_Tenant_Bs_document_chunks()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await SeedTenantWithChunkAsync(tenantAId, "Tenant A", "belongs to A");
        await SeedTenantWithChunkAsync(tenantBId, "Tenant B", "belongs to B");

        await using var contextAsTenantA = TestDbContextFactory.Create(fixture.ConnectionString, tenantAId);
        var visibleToA = await contextAsTenantA.DocumentChunks.ToListAsync();

        Assert.Single(visibleToA);
        Assert.All(visibleToA, c => Assert.Equal(tenantAId, c.TenantId));
    }
}
