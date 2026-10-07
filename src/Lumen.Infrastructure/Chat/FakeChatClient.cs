using Lumen.Application.Constants;

namespace Lumen.Infrastructure.Chat;

public class FakeChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var all = messages.ToList();
        var prompt = string.Concat(all.Select(m => m.Text));
        var hasSources = all.Any(m => m.Role == ChatRole.User && m.Text.Contains("<source ", StringComparison.Ordinal));
        var text = hasSources ? "Fake answer based on the provided sources [1]." : PromptConstants.RefusalSentence;

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = ChatConstants.FakeModelName,
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = Math.Max(1, prompt.Length / 4), OutputTokenCount = Math.Max(1, text.Length / 4) }
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Lumen does not stream answers.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
