namespace Lumen.UnitTests.Documents;

public class GetChunkContextQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDocumentChunkRepository> _chunks = new();
    private readonly Mock<IRepository<Document>> _documents = new();
    private readonly Guid _documentId = Guid.NewGuid();
    private readonly DocumentChunk _requested;

    public GetChunkContextQueryHandlerTests()
    {
        _requested = new DocumentChunk { Id = Guid.NewGuid(), DocumentId = _documentId, ChunkIndex = 5, Text = "middle" };
        _unitOfWork.SetupGet(u => u.Chunks).Returns(_chunks.Object);
        _unitOfWork.SetupGet(u => u.Documents).Returns(_documents.Object);
        _chunks.Setup(c => c.GetByIdAsync(_requested.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_requested);
        _documents.Setup(d => d.GetByIdAsync(_documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document { Id = _documentId, Title = "Apple 10-K", FilingDate = new DateOnly(2025, 9, 27) });
        _chunks.Setup(c => c.GetByIndexRangeAsync(_documentId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_requested]);
    }

    private GetChunkContextQueryHandler CreateHandler() => new(_unitOfWork.Object);

    [Fact]
    public async Task Loads_the_window_around_the_requested_chunk()
    {
        var context = await CreateHandler().Handle(new GetChunkContextQuery(_requested.Id, 2, 1), CancellationToken.None);

        _chunks.Verify(c => c.GetByIndexRangeAsync(_documentId, 3, 6, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(_documentId, context.DocumentId);
        Assert.Equal("Apple 10-K", context.DocumentTitle);
        Assert.Equal(new DateOnly(2025, 9, 27), context.DocumentFilingDate);
        Assert.Equal(_requested.Id, context.RequestedChunkId);
    }

    [Fact]
    public async Task Clamps_the_window_start_at_zero()
    {
        _requested.ChunkIndex = 1;

        await CreateHandler().Handle(new GetChunkContextQuery(_requested.Id, 3, 0), CancellationToken.None);

        _chunks.Verify(c => c.GetByIndexRangeAsync(_documentId, 0, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_or_foreign_chunk_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateHandler().Handle(new GetChunkContextQuery(Guid.NewGuid(), 1, 1), CancellationToken.None));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(4, 1)]
    [InlineData(1, 4)]
    public async Task Rejects_a_window_outside_zero_to_three(int before, int after)
    {
        await Assert.ThrowsAsync<Lumen.Application.Exceptions.ValidationException>(
            () => CreateHandler().Handle(new GetChunkContextQuery(_requested.Id, before, after), CancellationToken.None));

        _chunks.Verify(c => c.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
