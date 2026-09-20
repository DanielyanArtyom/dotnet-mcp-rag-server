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

        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 1, Text = "second", TokenCount = 1, CharStart = 10, CharEnd = 16, ChunkType = ChunkType.Prose });
        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 0, Text = "first", TokenCount = 1, CharStart = 0, CharEnd = 5, ChunkType = ChunkType.Prose });
        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = otherDocumentId, ChunkIndex = 0, Text = "other doc", TokenCount = 2, CharStart = 0, CharEnd = 9, ChunkType = ChunkType.Prose });
        await unitOfWork.CompleteAsync();

        var chunks = await unitOfWork.Chunks.GetByDocumentIdAsync(documentId);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("first", chunks[0].Text);
        Assert.Equal("second", chunks[1].Text);
    }
}
