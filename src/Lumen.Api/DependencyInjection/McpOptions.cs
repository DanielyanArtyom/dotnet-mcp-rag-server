namespace Lumen.Api.DependencyInjection;

public class McpOptions
{
    public const string SectionName = "Mcp";

    public bool ContextTool { get; set; }
    public string[] AllowedOrigins { get; set; } = [];
}
