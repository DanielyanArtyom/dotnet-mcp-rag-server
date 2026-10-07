namespace Lumen.UnitTests.Ask;

public class AskQuestionQueryHandlerTests
{
    private readonly Mock<IEmbeddingService> _embeddings = new();
    private readonly Mock<IChunkSearchRepository> _chunks = new();
    private readonly Mock<IChatService> _chat = new();

    public AskQuestionQueryHandlerTests()
    {
        _embeddings.SetupGet(e => e.ModelName).Returns("fake-1536");
        _embeddings.Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new float[1536]]);
        _chat.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedAnswer { Text = "Net sales were $416,161 million [1].", InputTokens = 900, OutputTokens = 20 });
    }

    private void Found(params string[] texts) =>
        _chunks.Setup(r => r.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(texts.Select((t, i) => new ChunkSearchResult
            {
                ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), DocumentTitle = "Apple 10-K", DocumentFilingDate = new DateOnly(2023, 10, 27),
                ChunkIndex = i, TokenCount = 50, Text = t, Distance = 0.1 + i * 0.01
            }).ToList());

    private AskQuestionQueryHandler CreateHandler() =>
        new(_embeddings.Object, _chunks.Object, _chat.Object, NullLogger<AskQuestionQueryHandler>.Instance);

    [Fact]
    public async Task Retrieves_with_the_given_top_k_and_document_then_generates()
    {
        Found("Total net sales 416,161", "Other");
        var documentId = Guid.NewGuid();

        var answer = await CreateHandler().Handle(new AskQuestionQuery("What were net sales?", documentId, 7), CancellationToken.None);

        _chunks.Verify(r => r.SearchAsync(It.IsAny<float[]>(), "What were net sales?", "fake-1536", 7, documentId, It.IsAny<CancellationToken>()), Times.Once);
        _chat.Verify(c => c.CompleteAsync(PromptConstants.SystemPrompt, It.Is<string>(u => u.Contains("Total net sales 416,161") && u.Contains("What were net sales?")), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Net sales were $416,161 million [1].", answer.Answer);
        Assert.False(answer.Refused);
        Assert.Equal(new[] { 1, 2 }, answer.Sources.Select(s => s.SourceId));
        Assert.True(answer.Sources[0].Cited);
        Assert.False(answer.Sources[1].Cited);
        Assert.Equal(50, answer.Sources[0].TokenCount);
        Assert.Equal((900, 20), (answer.InputTokens, answer.OutputTokens));
        Assert.True(answer.TotalMs >= answer.RetrievalMs);
    }

    [Fact]
    public async Task Refuses_without_calling_the_model_when_nothing_is_retrieved()
    {
        Found();

        var answer = await CreateHandler().Handle(new AskQuestionQuery("Anything?", null, 5), CancellationToken.None);

        Assert.True(answer.Refused);
        Assert.Equal(PromptConstants.RefusalSentence, answer.Answer);
        Assert.Empty(answer.Sources);
        _chat.Verify(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Flags_a_refusal_produced_by_the_model()
    {
        Found("Unrelated text");
        _chat.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedAnswer { Text = PromptConstants.RefusalSentence });

        var answer = await CreateHandler().Handle(new AskQuestionQuery("What is CET1?", null, 5), CancellationToken.None);

        Assert.True(answer.Refused);
        Assert.All(answer.Sources, s => Assert.False(s.Cited));
    }

    [Fact]
    public async Task Ignores_citations_to_sources_that_were_not_sent()
    {
        Found("Only one source");
        _chat.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedAnswer { Text = "Claim [4]. Real [1][1]." });

        var answer = await CreateHandler().Handle(new AskQuestionQuery("q", null, 5), CancellationToken.None);

        Assert.Single(answer.Sources);
        Assert.True(answer.Sources[0].Cited);
    }

    [Fact]
    public async Task Propagates_truncation()
    {
        Found("Some text");
        _chat.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedAnswer { Text = "", Truncated = true });

        var answer = await CreateHandler().Handle(new AskQuestionQuery("q", null, 5), CancellationToken.None);

        Assert.True(answer.Truncated);
        Assert.False(answer.Refused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_a_blank_question(string question) =>
        await Assert.ThrowsAsync<Lumen.Application.Exceptions.ValidationException>(
            () => CreateHandler().Handle(new AskQuestionQuery(question, null, 5), CancellationToken.None));

    [Fact]
    public async Task Rejects_a_question_longer_than_2000_characters() =>
        await Assert.ThrowsAsync<Lumen.Application.Exceptions.ValidationException>(
            () => CreateHandler().Handle(new AskQuestionQuery(new string('a', 2001), null, 5), CancellationToken.None));

    [Fact]
    public async Task Wraps_chat_failures_in_ExternalServiceException()
    {
        Found("Some text");
        _chat.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        await Assert.ThrowsAsync<ExternalServiceException>(
            () => CreateHandler().Handle(new AskQuestionQuery("q", null, 5), CancellationToken.None));
    }

    [Fact]
    public async Task Wraps_embedding_failures_and_never_searches()
    {
        _embeddings.Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429"));

        await Assert.ThrowsAsync<ExternalServiceException>(
            () => CreateHandler().Handle(new AskQuestionQuery("q", null, 5), CancellationToken.None));

        _chunks.Verify(r => r.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
