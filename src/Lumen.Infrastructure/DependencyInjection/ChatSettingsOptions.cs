namespace Lumen.Infrastructure.DependencyInjection;

public class ChatSettingsOptions
{
    public const string SectionName = "Chat";

    public ChatProvider Provider { get; set; } = ChatProvider.Fake;
    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public int MaxOutputTokens { get; set; } = 1024;
    public int ContextTopK { get; set; } = 5;
}
