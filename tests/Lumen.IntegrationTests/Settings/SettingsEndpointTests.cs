namespace Lumen.IntegrationTests.Settings;

[Collection("Postgres collection")]
public class SettingsEndpointTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _baseFactory = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _baseFactory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _factory = _baseFactory.WithWebHostBuilder(b =>
            b.UseSetting("Chat:ApiKey", "super-secret-chat-key"));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _baseFactory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Returns_the_effective_configuration_without_secrets()
    {
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Settings admin", Role.Admin);
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);

        var response = await client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("super-secret-chat-key", raw);
        Assert.DoesNotContain("apiKey", raw, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        Assert.Equal("Fake", root.GetProperty("chat").GetProperty("provider").GetString());
        Assert.Equal(ChatConstants.FakeModelName, root.GetProperty("chat").GetProperty("deployment").GetString());
        Assert.Equal(10, root.GetProperty("chat").GetProperty("contextTopK").GetInt32());
        Assert.False(root.GetProperty("chat").GetProperty("sourceContextHeader").GetBoolean());
        Assert.Equal("Fake", root.GetProperty("embeddings").GetProperty("provider").GetString());
        Assert.Equal(EmbeddingConstants.FakeModelName, root.GetProperty("embeddings").GetProperty("model").GetString());
        Assert.Equal(250, root.GetProperty("chunking").GetProperty("maxTokensPerChunk").GetInt32());
        Assert.False(root.GetProperty("chunking").GetProperty("tableCells").GetBoolean());
        Assert.Equal("Pipe", root.GetProperty("chunking").GetProperty("tableFormat").GetString());
        Assert.Equal("vector", root.GetProperty("searchMode").GetString());
        Assert.Matches("^[0-9a-f]{12}$", root.GetProperty("promptHash").GetString());
        Assert.False(root.GetProperty("mcp").GetProperty("contextTool").GetBoolean());
    }

    [Fact]
    public async Task Is_admin_only()
    {
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Settings member", Role.Member);
        using var member = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(member, email, password);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/settings")).StatusCode);
    }
}
