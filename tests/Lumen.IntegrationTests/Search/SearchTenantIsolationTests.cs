namespace Lumen.IntegrationTests.Search;

[Collection("Postgres collection")]
public class SearchTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private (string Email, string Password) _tenantA;
    private (string Email, string Password) _tenantB;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, emailA, passwordA) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Search isolation A", Role.Admin);
        var (_, emailB, passwordB) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Search isolation B", Role.Admin);
        _tenantA = (emailA, passwordA);
        _tenantB = (emailB, passwordB);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task UploadAsync(HttpClient client, string title, string body)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");

        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
    }

    // Exercises the raw-SQL repository's hand-written tenant predicate directly (this endpoint's search
    // query can never be reached by EF Core's global TenantId query filter), the same way
    // DocumentChunkTenantIsolationTests exercises it for the mapped-entity read path.
    [Fact]
    public async Task Tenant_As_search_never_returns_Tenant_Bs_chunks()
    {
        using var clientB = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientB, _tenantB.Email, _tenantB.Password);
        await UploadAsync(clientB, "Tenant B filing", "<p>Confidential tenant B figures.</p>");

        using var clientA = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientA, _tenantA.Email, _tenantA.Password);
        await UploadAsync(clientA, "Tenant A filing", "<p>Public tenant A figures.</p>");

        var response = await clientA.PostAsJsonAsync("/api/search", new { query = "Confidential tenant B figures.", topK = 10 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<SearchResultDto>>();
        Assert.NotEmpty(results!);
        Assert.All(results!, r => Assert.Equal("Tenant A filing", r.DocumentTitle));
        Assert.DoesNotContain(results!, r => r.Text.Contains("tenant B", StringComparison.OrdinalIgnoreCase));
    }
}
