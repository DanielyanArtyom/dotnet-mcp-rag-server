using Lumen.Application.Search;

namespace Lumen.UnitTests.Search;

public class SearchChunksQueryHandlerTests
{
    private readonly Mock<IEmbeddingService> _embeddings = new();
    private readonly Mock<IChunkSearchRepository> _repository = new();

    public SearchChunksQueryHandlerTests()
    {
        _embeddings.SetupGet(e => e.ModelName).Returns("fake-1536");
        _embeddings.Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new float[1536]]);
        _repository.Setup(r => r.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private SearchChunksQueryHandler CreateHandler() => new(_embeddings.Object, _repository.Object);

    [Fact]
    public async Task Embeds_the_query_once_and_passes_the_parameters_through()
    {
        var documentId = Guid.NewGuid();

        await CreateHandler().Handle(new SearchChunksQuery("net sales", 7, documentId), CancellationToken.None);

        _embeddings.Verify(e => e.EmbedAsync(It.Is<IReadOnlyList<string>>(t => t.Count == 1 && t[0] == "net sales"), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.SearchAsync(It.IsAny<float[]>(), "net sales", "fake-1536", 7, documentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_a_blank_query(string query)
    {
        await Assert.ThrowsAsync<Lumen.Application.Exceptions.ValidationException>(
            () => CreateHandler().Handle(new SearchChunksQuery(query, 5, null), CancellationToken.None));

        _embeddings.Verify(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Returns_what_the_repository_found()
    {
        var found = new List<ChunkSearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentTitle = "Apple 10-K", Text = "Total net sales", Distance = 0.02 }
        };
        _repository.Setup(r => r.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(found);

        var results = await CreateHandler().Handle(new SearchChunksQuery("net sales", 5, null), CancellationToken.None);

        Assert.Same(found, results);
    }

    [Fact]
    public async Task Wraps_embedding_failures_in_ExternalServiceException_and_never_queries_the_repository()
    {
        _embeddings.Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429 Too Many Requests"));

        await Assert.ThrowsAsync<ExternalServiceException>(
            () => CreateHandler().Handle(new SearchChunksQuery("net sales", 5, null), CancellationToken.None));

        _repository.Verify(r => r.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
