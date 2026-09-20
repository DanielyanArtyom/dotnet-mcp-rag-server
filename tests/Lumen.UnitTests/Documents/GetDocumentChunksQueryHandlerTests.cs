namespace Lumen.UnitTests.Documents;

public class GetDocumentChunksQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_chunks_for_the_requested_document()
    {
        var documentId = Guid.NewGuid();
        var chunks = new List<DocumentChunk> { new() { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 0, Text = "x", TokenCount = 1 } };

        var chunkRepository = new Mock<IDocumentChunkRepository>();
        chunkRepository.Setup(r => r.GetByDocumentIdAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync(chunks);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Chunks).Returns(chunkRepository.Object);

        var handler = new GetDocumentChunksQueryHandler(unitOfWork.Object);

        var result = await handler.Handle(new GetDocumentChunksQuery(documentId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(documentId, result[0].DocumentId);
    }
}
