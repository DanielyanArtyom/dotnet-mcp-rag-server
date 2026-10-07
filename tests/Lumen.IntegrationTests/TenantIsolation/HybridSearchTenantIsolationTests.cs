namespace Lumen.IntegrationTests.TenantIsolation;

[Collection("Postgres collection")]
public class HybridSearchTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string SharedText = "The zebracorn index closed the year higher.";

    private WebApplicationFactory<Program> _baseFactory = null!;
    private (Guid TenantId, string Email, string Password) _tenantA;
    private (Guid TenantId, string Email, string Password) _tenantB;

    public async Task InitializeAsync()
    {
        _baseFactory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _tenantA = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Hybrid isolation A", Role.Admin);
        _tenantB = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Hybrid isolation B", Role.Admin);
    }

    public Task DisposeAsync()
    {
        _baseFactory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> UploadAsync(HttpClient client, string title, string text = SharedText)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body><p>{text}</p></body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");

        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!.Id;
    }

    private async Task AssertNeverReturnsTenantAAsync(params (string Key, string Value)[] settings)
    {
        using var factory = _baseFactory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Search:Mode", "Hybrid");
            foreach (var (key, value) in settings)
            {
                b.UseSetting(key, value);
            }
        });

        using var clientA = factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientA, _tenantA.Email, _tenantA.Password);
        var documentA = await UploadAsync(clientA, "A filing");

        using var clientB = factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientB, _tenantB.Email, _tenantB.Password);
        var documentB = await UploadAsync(clientB, "B filing");

        var results = (await (await clientB.PostAsJsonAsync("/api/search", new { query = "zebracorn", topK = 10 }))
            .Content.ReadFromJsonAsync<List<SearchResultDto>>())!;

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(documentB, r.DocumentId));
        Assert.DoesNotContain(results, r => r.DocumentId == documentA);
    }

    [Fact]
    public Task Hybrid_search_never_returns_another_tenants_lexical_match() =>
        AssertNeverReturnsTenantAAsync(("Search:VectorWeight", "0"));

    [Fact]
    public Task Hybrid_search_never_returns_another_tenants_chunk_for_identical_text() =>
        AssertNeverReturnsTenantAAsync();

    [Fact]
    public async Task Lexical_candidates_are_ranked_within_the_callers_tenant_only()
    {
        using var factory = _baseFactory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Search:Mode", "Hybrid");
            b.UseSetting("Search:Candidates", "1");
            b.UseSetting("Search:VectorWeight", "0");
        });

        using var clientA = factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientA, _tenantA.Email, _tenantA.Password);
        await UploadAsync(clientA, "A filing", string.Join(" ", Enumerable.Repeat("zebracorn", 40)));

        using var clientB = factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientB, _tenantB.Email, _tenantB.Password);
        var documentB = await UploadAsync(clientB, "B filing", SharedText);

        var results = (await (await clientB.PostAsJsonAsync("/api/search", new { query = "zebracorn", topK = 1 }))
            .Content.ReadFromJsonAsync<List<SearchResultDto>>())!;

        var result = Assert.Single(results);
        Assert.Equal(documentB, result.DocumentId);
        Assert.True(result.Score > 0, "tenant B's lexical rank was crowded out by tenant A's chunk");
    }
}
