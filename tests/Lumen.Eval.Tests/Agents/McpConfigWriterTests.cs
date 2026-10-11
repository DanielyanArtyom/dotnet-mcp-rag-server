namespace Lumen.Eval.Tests.Agents;

public class McpConfigWriterTests
{
    [Fact]
    public void Writes_one_http_server_named_lumen_with_the_bearer_header()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;

        var path = McpConfigWriter.Write(dir, "http://localhost:5119/mcp", "tok");

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var lumen = json.RootElement.GetProperty("mcpServers").GetProperty("lumen");
        Assert.Equal("http", lumen.GetProperty("type").GetString());
        Assert.Equal("http://localhost:5119/mcp", lumen.GetProperty("url").GetString());
        Assert.Equal("Bearer tok", lumen.GetProperty("headers").GetProperty("Authorization").GetString());
        Directory.Delete(dir, recursive: true);
    }
}
