using Microsoft.Extensions.Logging;

namespace Lumen.UnitTests.Chat;

public class ChatServiceTests
{
    private readonly Mock<IChatClient> _client = new();
    private ChatOptions? _sentOptions;
    private List<ChatMessage>? _sentMessages;

    private readonly Mock<ILogger<ChatService>> _logger = new();

    private ChatService Service(ChatSettingsOptions options) => new(_client.Object, options, _logger.Object);

    private void VerifyWarnings(Times times) =>
        _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    private void Returns(ChatResponse response) =>
        _client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, o, _) => { _sentMessages = m.ToList(); _sentOptions = o; })
            .ReturnsAsync(response);

    [Fact]
    public async Task Sends_a_system_and_a_user_message_and_maps_usage()
    {
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Answer [1]"))
        {
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30, ReasoningTokenCount = 10 }
        });

        var result = await Service(new ChatSettingsOptions()).CompleteAsync("sys", "user");

        Assert.Equal(new[] { ChatRole.System, ChatRole.User }, _sentMessages!.Select(m => m.Role));
        Assert.Equal("Answer [1]", result.Text);
        Assert.Equal((120, 30, 10), (result.InputTokens, result.OutputTokens, result.ReasoningTokens));
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Leaves_unset_sampling_values_out_of_the_request()
    {
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "x")));

        await Service(new ChatSettingsOptions { MaxOutputTokens = 512 }).CompleteAsync("sys", "user");

        Assert.Null(_sentOptions!.Temperature);
        Assert.Null(_sentOptions.TopP);
        Assert.Null(_sentOptions.TopK);
        Assert.Null(_sentOptions.Reasoning);
        Assert.Equal(512, _sentOptions.MaxOutputTokens);
    }

    [Fact]
    public async Task Passes_configured_sampling_values_and_reasoning_effort()
    {
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "x")));
        var options = new ChatSettingsOptions { Temperature = 0.2f, TopP = 0.9f, ReasoningEffort = ReasoningEffort.None };

        await Service(options).CompleteAsync("sys", "user");

        Assert.Equal(0.2f, _sentOptions!.Temperature);
        Assert.Equal(0.9f, _sentOptions.TopP);
        Assert.Equal(ReasoningEffort.None, _sentOptions.Reasoning!.Effort);
    }

    [Fact]
    public async Task Reports_truncation_when_the_model_hit_the_token_limit()
    {
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")) { FinishReason = ChatFinishReason.Length });

        var result = await Service(new ChatSettingsOptions()).CompleteAsync("sys", "user");

        Assert.True(result.Truncated);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task Warns_when_the_model_returns_empty_text_without_truncation()
    {
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")) { FinishReason = ChatFinishReason.Stop });

        await Service(new ChatSettingsOptions()).CompleteAsync("sys", "user");

        VerifyWarnings(Times.Once());
    }

    [Fact]
    public async Task Warns_on_a_content_filter_finish_reason()
    {
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "partial")) { FinishReason = ChatFinishReason.ContentFilter });

        await Service(new ChatSettingsOptions()).CompleteAsync("sys", "user");

        VerifyWarnings(Times.Once());
    }

    [Fact]
    public async Task Does_not_warn_for_a_normal_or_truncated_response()
    {
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "fine")) { FinishReason = ChatFinishReason.Stop });
        await Service(new ChatSettingsOptions()).CompleteAsync("sys", "user");
        Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")) { FinishReason = ChatFinishReason.Length });
        await Service(new ChatSettingsOptions()).CompleteAsync("sys", "user");

        VerifyWarnings(Times.Never());
    }
}
