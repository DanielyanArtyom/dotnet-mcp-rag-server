namespace Lumen.UnitTests.Domain;

public class DocumentChunkTests
{
    [Fact]
    public void DocumentChunk_holds_the_values_it_was_given()
    {
        var id = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var chunk = new DocumentChunk
        {
            Id = id,
            DocumentId = documentId,
            TenantId = tenantId,
            ChunkIndex = 3,
            Text = "Net sales increased 16% year over year.",
            TokenCount = 42,
            CharStart = 100,
            CharEnd = 250,
            ChunkType = ChunkType.Prose
        };

        Assert.Equal(id, chunk.Id);
        Assert.Equal(documentId, chunk.DocumentId);
        Assert.Equal(tenantId, chunk.TenantId);
        Assert.Equal(3, chunk.ChunkIndex);
        Assert.Equal(42, chunk.TokenCount);
        Assert.Equal(ChunkType.Prose, chunk.ChunkType);
    }

    [Fact]
    public void Document_holds_the_new_raw_file_and_filing_date_fields()
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Title = "Apple 10-K FY2025",
            Content = "cleaned text goes here",
            RawFilePath = "11111111-1111-1111-1111-111111111111/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.htm",
            FilingDate = new DateOnly(2025, 10, 31)
        };

        Assert.Equal("cleaned text goes here", document.Content);
        Assert.Equal(new DateOnly(2025, 10, 31), document.FilingDate);
    }
}
