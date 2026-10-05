namespace Lumen.Application.Interfaces;

public interface IChatService
{
    Task<GeneratedAnswer> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}
