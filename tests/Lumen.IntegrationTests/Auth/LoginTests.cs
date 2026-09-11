namespace Lumen.IntegrationTests.Auth;

[Collection("Postgres collection")]
public class LoginTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _email = null!;
    private string _password = null!;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        (_, _email, _password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Login test tenant");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Login_with_correct_credentials_returns_a_token()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { email = _email, password = _password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrWhiteSpace(body!["token"]));
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { email = _email, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
