namespace Lumen.IntegrationTests.Search;

[Collection("Postgres collection")]
public class HybridSearchTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _baseFactory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _baseFactory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _factory = _baseFactory.WithWebHostBuilder(b => b.UseSetting("Search:Mode", "Hybrid"));
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Hybrid tenant", Role.Admin);
        _client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_client, email, password);

        await UploadAsync("Filler one", "<p>Operating expenses grew with headcount and infrastructure costs.</p>");
        await UploadAsync("Filler two", "<p>Services revenue rose on advertising and cloud services.</p>");
        await UploadAsync("Rare word", "<p>The zebracorn index closed the year higher.</p>");
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        _baseFactory.Dispose();
        return Task.CompletedTask;
    }

    private async Task UploadAsync(string title, string body)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await _client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();
    }

    private async Task<List<SearchResultDto>> SearchAsync(string query) =>
        (await (await _client.PostAsJsonAsync("/api/search", new { query, topK = 3 })).Content.ReadFromJsonAsync<List<SearchResultDto>>())!;

    [Fact]
    public async Task Hybrid_search_ranks_a_lexical_only_match_first()
    {
        var results = await SearchAsync("Which index is called zebracorn?");

        Assert.Contains("zebracorn", results[0].Text);
        Assert.NotNull(results[0].Score);
    }

    [Fact]
    public async Task Hybrid_search_with_only_stop_words_falls_back_to_vector_ranks()
    {
        var results = await SearchAsync("What is it?");

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Hybrid_search_tolerates_tsquery_operators_in_the_question()
    {
        var response = await _client.PostAsJsonAsync("/api/search", new { query = "Apple's R&D (FY'25) | !cost: 'x' <-> zebracorn", topK = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Settings_report_the_hybrid_mode()
    {
        var settings = JsonDocument.Parse(await _client.GetStringAsync("/api/settings")).RootElement;

        Assert.Equal("hybrid", settings.GetProperty("searchMode").GetString());
        Assert.Equal(60, settings.GetProperty("search").GetProperty("rrfK").GetInt32());
    }
}
