namespace Lumen.Infrastructure.Chat;

public class ChatService : IChatService
{
    private readonly IChatClient _client;
    private readonly ChatSettingsOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(IChatClient client, ChatSettingsOptions options, ILogger<ChatService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<GeneratedAnswer> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        List<ChatMessage> messages = [new(ChatRole.System, systemPrompt), new(ChatRole.User, userMessage)];

        var response = await _client.GetResponseAsync(messages, BuildOptions(), ct);

        var truncated = response.FinishReason == ChatFinishReason.Length;
        if (response.FinishReason == ChatFinishReason.ContentFilter || (!truncated && string.IsNullOrWhiteSpace(response.Text)))
        {
            _logger.LogWarning("Chat model returned no usable answer (finish reason {FinishReason}, {Length} characters of text)",
                response.FinishReason, response.Text.Length);
        }

        return new GeneratedAnswer
        {
            Text = response.Text,
            InputTokens = (int)(response.Usage?.InputTokenCount ?? 0),
            OutputTokens = (int)(response.Usage?.OutputTokenCount ?? 0),
            ReasoningTokens = (int)(response.Usage?.ReasoningTokenCount ?? 0),
            Truncated = truncated
        };
    }

    private ChatOptions BuildOptions() => new()
    {
        Temperature = _options.Temperature,
        TopP = _options.TopP,
        MaxOutputTokens = _options.MaxOutputTokens,
        Reasoning = _options.ReasoningEffort is { } effort ? new ReasoningOptions { Effort = effort } : null
    };
}
