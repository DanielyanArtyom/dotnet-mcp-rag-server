namespace Lumen.Eval.Agents;

public static class McpConfigWriter
{
    public static string Write(string directory, string mcpUrl, string token)
    {
        var path = Path.Combine(directory, "mcp.json");
        var config = new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object>
            {
                ["lumen"] = new Dictionary<string, object>
                {
                    ["type"] = "http",
                    ["url"] = mcpUrl,
                    ["headers"] = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
                }
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return path;
    }
}
