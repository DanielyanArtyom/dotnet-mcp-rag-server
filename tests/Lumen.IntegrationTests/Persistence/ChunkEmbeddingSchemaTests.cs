namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class ChunkEmbeddingSchemaTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Embedding_column_is_a_1536_dimension_vector()
    {
        await using var context = TestDbContextFactory.Create(fixture.ConnectionString);

        var types = await context.Database
            .SqlQueryRaw<string>("""
                SELECT format_type(a.atttypid, a.atttypmod) AS "Value"
                FROM pg_attribute a
                WHERE a.attrelid = '"DocumentChunks"'::regclass AND a.attname = 'Embedding'
                """)
            .ToListAsync();

        Assert.Equal("vector(1536)", types.Single());
    }

    [Fact]
    public async Task A_chunk_round_trips_its_embedding_and_model_name()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        await using (var context = TestDbContextFactory.Create(fixture.ConnectionString, tenantId))
        {
            context.Tenants.Add(new TenantEntity { Id = tenantId, Name = "Embedding round trip" });
            context.Documents.Add(new DocumentEntity
            {
                Id = documentId,
                TenantId = tenantId,
                Title = "Filing",
                Content = "Body",
                RawFilePath = $"{tenantId}/{documentId}.htm",
                FilingDate = new DateOnly(2025, 1, 1)
            });
            context.DocumentChunks.Add(new DocumentChunkEntity
            {
                Id = chunkId,
                TenantId = tenantId,
                DocumentId = documentId,
                ChunkIndex = 0,
                Text = "Total net sales",
                TokenCount = 3,
                ChunkType = ChunkType.Prose,
                Embedding = new Vector(TestVectors.OneHot(7)),
                EmbeddingModel = "fake-1536"
            });
            await context.SaveChangesAsync();
        }

        await using var readContext = TestDbContextFactory.Create(fixture.ConnectionString, tenantId);
        var stored = await readContext.DocumentChunks.SingleAsync(c => c.Id == chunkId);

        Assert.Equal("fake-1536", stored.EmbeddingModel);
        Assert.Equal(1536, stored.Embedding.ToArray().Length);
        Assert.Equal(1f, stored.Embedding.ToArray()[7]);
    }
}
