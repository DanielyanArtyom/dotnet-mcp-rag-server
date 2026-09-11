namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class CreateDocumentValidationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Validation");

        _client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_client, email, password);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Creating_a_document_with_a_title_over_500_characters_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/documents", new { title = new string('x', 501), content = "body" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_document_without_content_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/documents", new { title = "A title" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_valid_document_still_returns_201()
    {
        var response = await _client.PostAsJsonAsync("/api/documents", new { title = "A title", content = "body" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
