namespace Lumen.UnitTests.Mcp;

public class ChunkContextToolTests
{
    private readonly Mock<IMediator> _mediator = new();

    [Fact]
    public async Task Passes_the_parsed_chunkId_and_window()
    {
        var chunkId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<GetChunkContextQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkContext { DocumentTitle = "T", RequestedChunkId = chunkId, Chunks = [] });

        await new ChunkContextTool(_mediator.Object).GetChunkContextAsync(chunkId.ToString(), 2, 0);

        _mediator.Verify(m => m.Send(It.Is<GetChunkContextQuery>(q => q.ChunkId == chunkId && q.Before == 2 && q.After == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_malformed_chunkId_with_a_hint()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => new ChunkContextTool(_mediator.Object).GetChunkContextAsync("not-a-guid"));

        Assert.Contains("search_filings", ex.Message);
    }
}
