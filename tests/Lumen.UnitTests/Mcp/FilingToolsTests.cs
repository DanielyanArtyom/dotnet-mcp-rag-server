namespace Lumen.UnitTests.Mcp;

public class FilingToolsTests
{
    private readonly Mock<IMediator> _mediator = new();

    public FilingToolsTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<SearchChunksQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _mediator.Setup(m => m.Send(It.IsAny<GetDocumentsQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private FilingTools CreateTools() => new(_mediator.Object);

    [Fact]
    public async Task Search_passes_query_topK_and_parsed_documentId()
    {
        var documentId = Guid.NewGuid();

        await CreateTools().SearchFilingsAsync("net sales", 7, documentId.ToString());

        _mediator.Verify(m => m.Send(It.Is<SearchChunksQuery>(q => q.Query == "net sales" && q.TopK == 7 && q.DocumentId == documentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Search_defaults_to_ten_results_across_all_filings()
    {
        await CreateTools().SearchFilingsAsync("net sales");

        _mediator.Verify(m => m.Send(It.Is<SearchChunksQuery>(q => q.TopK == McpConstants.DefaultTopK && q.DocumentId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Search_rejects_topK_outside_one_to_twenty(int topK)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateTools().SearchFilingsAsync("q", topK));

        Assert.Contains("between 1 and 20", ex.Message);
        _mediator.Verify(m => m.Send(It.IsAny<SearchChunksQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Search_rejects_a_malformed_documentId_with_a_hint()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateTools().SearchFilingsAsync("q", 5, "apple-10k"));

        Assert.Contains("list_filings", ex.Message);
    }

    [Fact]
    public async Task Search_returns_the_hint_when_nothing_matches()
    {
        Assert.Equal(McpConstants.NoResultsText, await CreateTools().SearchFilingsAsync("q"));
    }

    [Fact]
    public async Task List_formats_the_tenants_documents()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<GetDocumentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Document { Id = id, Title = "Apple 10-K", FilingDate = new DateOnly(2025, 9, 27) }]);

        Assert.Equal($"- Apple 10-K — filed 2025-09-27 — documentId: {id}", await CreateTools().ListFilingsAsync(CancellationToken.None));
    }
}
