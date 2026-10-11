namespace Lumen.IntegrationTests.Repositories;

[Collection("Postgres collection")]
public class DocumentChunkRepositoryTests(PostgresFixture fixture)
{
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<DomainEntityMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task GetByDocumentIdAsync_returns_only_that_documents_chunks_in_order()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var otherDocumentId = Guid.NewGuid();

        await using var context = TestDbContextFactory.Create(fixture.ConnectionString, tenantId);
        context.Tenants.Add(new TenantEntity { Id = tenantId, Name = "Chunk repository test tenant" });
        context.Documents.Add(new DocumentEntity { Id = documentId, TenantId = tenantId, Title = "T", Content = "C", RawFilePath = "x.htm", FilingDate = new DateOnly(2025, 1, 1) });
        context.Documents.Add(new DocumentEntity { Id = otherDocumentId, TenantId = tenantId, Title = "T2", Content = "C2", RawFilePath = "y.htm", FilingDate = new DateOnly(2025, 1, 1) });
        await context.SaveChangesAsync();

        var repository = new DocumentChunkRepository(context, Mapper);
        var unitOfWork = new UnitOfWork(context, new DocumentRepository(context, Mapper), repository);

        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 1, Text = "second", TokenCount = 1, CharStart = 10, CharEnd = 16, ChunkType = ChunkType.Prose, Embedding = TestVectors.OneHot(1), EmbeddingModel = "fake-1536" });
        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 0, Text = "first", TokenCount = 1, CharStart = 0, CharEnd = 5, ChunkType = ChunkType.Prose, Embedding = TestVectors.OneHot(0), EmbeddingModel = "fake-1536" });
        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = otherDocumentId, ChunkIndex = 0, Text = "other doc", TokenCount = 2, CharStart = 0, CharEnd = 9, ChunkType = ChunkType.Prose, Embedding = TestVectors.OneHot(2), EmbeddingModel = "fake-1536" });
        await unitOfWork.CompleteAsync();

        var chunks = await unitOfWork.Chunks.GetByDocumentIdAsync(documentId);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("first", chunks[0].Text);
        Assert.Equal("second", chunks[1].Text);
    }

    [Fact]
    public async Task GetByIndexRangeAsync_returns_the_inclusive_window_in_order()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await using var context = TestDbContextFactory.Create(fixture.ConnectionString, tenantId);
        context.Tenants.Add(new TenantEntity { Id = tenantId, Name = "Range tenant" });
        context.Documents.Add(new DocumentEntity { Id = documentId, TenantId = tenantId, Title = "T", Content = "C", RawFilePath = "r.htm", FilingDate = new DateOnly(2025, 1, 1) });
        await context.SaveChangesAsync();

        var repository = new DocumentChunkRepository(context, Mapper);
        for (var i = 0; i < 6; i++)
        {
            repository.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = i, Text = $"chunk {i}", TokenCount = 2, ChunkType = ChunkType.Prose, Embedding = TestVectors.OneHot(i), EmbeddingModel = "fake-1536" });
        }
        await context.SaveChangesAsync();

        var window = await repository.GetByIndexRangeAsync(documentId, 2, 4);

        Assert.Equal(["chunk 2", "chunk 3", "chunk 4"], window.Select(c => c.Text));
    }

    [Fact]
    public async Task GetByIndexRangeAsync_never_returns_another_tenants_chunks()
    {
        var ownerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await using (var owner = TestDbContextFactory.Create(fixture.ConnectionString, ownerId))
        {
            owner.Tenants.Add(new TenantEntity { Id = ownerId, Name = "Range owner" });
            owner.Documents.Add(new DocumentEntity { Id = documentId, TenantId = ownerId, Title = "T", Content = "C", RawFilePath = "o.htm", FilingDate = new DateOnly(2025, 1, 1) });
            owner.DocumentChunks.Add(new DocumentChunkEntity { Id = Guid.NewGuid(), DocumentId = documentId, TenantId = ownerId, ChunkIndex = 0, Text = "secret", TokenCount = 1, ChunkType = ChunkType.Prose, Embedding = new Vector(TestVectors.OneHot(0)), EmbeddingModel = "fake-1536" });
            await owner.SaveChangesAsync();
        }

        var otherId = Guid.NewGuid();
        await using var other = TestDbContextFactory.Create(fixture.ConnectionString, otherId);
        other.Tenants.Add(new TenantEntity { Id = otherId, Name = "Range intruder" });
        await other.SaveChangesAsync();

        var window = await new DocumentChunkRepository(other, Mapper).GetByIndexRangeAsync(documentId, 0, 10);

        Assert.Empty(window);
    }
}
