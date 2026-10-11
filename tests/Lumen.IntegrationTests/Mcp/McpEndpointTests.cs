namespace Lumen.IntegrationTests.Mcp;

[Collection("Postgres collection")]
public class McpEndpointTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private (Guid TenantId, string Email, string Password) _member;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _member = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Mcp member tenant", Role.Member);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> MemberTokenAsync()
    {
        using var client = _factory.CreateClient();
        return await TestAuth.LoginAsync(client, _member.Email, _member.Password);
    }

    [Fact]
    public async Task Without_a_token_the_endpoint_answers_401_with_a_bearer_challenge()
    {
        using var http = _factory.CreateClient();

        var response = await TestMcp.PostRawAsync(http, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task A_foreign_origin_is_forbidden_even_with_a_valid_token()
    {
        using var http = _factory.CreateClient();

        var response = await TestMcp.PostRawAsync(http, await MemberTokenAsync(), origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_member_lists_the_read_only_tools_without_the_context_tool_by_default()
    {
        await using var client = await TestMcp.ConnectAsync(_factory, await MemberTokenAsync());

        var tools = await client.ListToolsAsync();

        Assert.Equal(["list_filings", "search_filings"], tools.Select(t => t.Name).Order());
        Assert.All(tools, t =>
        {
            Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.True(t.ProtocolTool.Annotations?.IdempotentHint);
            Assert.False(t.ProtocolTool.Annotations?.DestructiveHint);
            Assert.False(t.ProtocolTool.Annotations?.OpenWorldHint);
        });
    }

    [Fact]
    public async Task The_context_tool_appears_when_switched_on()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Mcp:ContextTool", "true"));
        using var http = factory.CreateClient();
        var token = await TestAuth.LoginAsync(http, _member.Email, _member.Password);
        await using var client = await TestMcp.ConnectAsync(factory, token);

        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, t => t.Name == "get_chunk_context");
    }

    [Fact]
    public async Task Invalid_arguments_come_back_as_an_actionable_tool_error()
    {
        await using var client = await TestMcp.ConnectAsync(_factory, await MemberTokenAsync());

        var result = await client.CallToolAsync("search_filings", new Dictionary<string, object?> { ["query"] = "net sales", ["topK"] = 50 });

        Assert.True(result.IsError);
        Assert.Contains("between 1 and 20", TestMcp.TextOf(result));
    }

    [Fact]
    public async Task An_embedding_outage_is_reported_as_retryable()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddScoped<IEmbeddingService, ThrowingEmbeddingService>()));
        using var http = factory.CreateClient();
        var token = await TestAuth.LoginAsync(http, _member.Email, _member.Password);
        await using var client = await TestMcp.ConnectAsync(factory, token);

        var result = await client.CallToolAsync("search_filings", new Dictionary<string, object?> { ["query"] = "net sales" });

        Assert.True(result.IsError);
        Assert.Equal("Search is temporarily unavailable. This is retryable.", TestMcp.TextOf(result));
    }

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public string ModelName => "fake-1536";

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            throw new HttpRequestException("provider down");
    }
}
