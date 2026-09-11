namespace Lumen.IntegrationTests.Auth;

[Collection("Postgres collection")]
public class TenantResolutionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Anonymous_request_to_a_protected_endpoint_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
