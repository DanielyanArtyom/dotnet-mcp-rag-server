namespace Lumen.IntegrationTests.Search;

[Collection("Postgres collection")]
public class DiversityCapTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _baseFactory = null!;
    private string _email = null!;
    private string _password = null!;
    private Guid _big;
    private Guid _small;

    public async Task InitializeAsync()
    {
        _baseFactory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Diversity tenant", Role.Admin);
        _email = email;
        _password = password;

        using var client = _baseFactory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _email, _password);
        _big = await UploadAsync(client, "Big", 6);
        _small = await UploadAsync(client, "Small", 2);
    }

    public Task DisposeAsync()
    {
        _baseFactory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string title, int tables)
    {
        var body = string.Concat(Enumerable.Range(1, tables)
            .Select(i => $"<table><tr><td>Total net sales {i}</td><td>{i}00</td></tr></table>"));
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!.Id;
    }

    private async Task<List<SearchResultDto>> SearchAsync(string mode, int? maxPerDocument, Guid? documentId = null)
    {
        using var factory = _baseFactory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Search:Mode", mode);
            if (maxPerDocument is not null)
                b.UseSetting("Search:MaxChunksPerDocument", maxPerDocument.Value.ToString());
        });
        using var client = factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _email, _password);
        var response = await client.PostAsJsonAsync("/api/search", new { query = "Total net sales", topK = 5, documentId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<SearchResultDto>>())!;
    }

    [Theory]
    [InlineData("Vector")]
    [InlineData("Hybrid")]
    public async Task At_most_three_of_five_come_from_one_document_when_another_has_candidates(string mode)
    {
        var results = await SearchAsync(mode, maxPerDocument: 3);

        Assert.Equal(5, results.Count);
        Assert.True(results.Count(r => r.DocumentId == _big) <= 3);
        Assert.Equal(2, results.Count(r => r.DocumentId == _small));
    }

    [Fact]
    public async Task Without_a_cap_results_are_unchanged()
    {
        var uncapped = await SearchAsync("Vector", maxPerDocument: null);
        var wideCap = await SearchAsync("Vector", maxPerDocument: 99);

        Assert.Equal(5, uncapped.Count);
        Assert.Equal(uncapped.Select(r => r.ChunkId), wideCap.Select(r => r.ChunkId));
    }

    [Theory]
    [InlineData("Vector")]
    [InlineData("Hybrid")]
    public async Task Cap_backfills_when_only_one_document_has_candidates(string mode)
    {
        var results = await SearchAsync(mode, maxPerDocument: 3, documentId: _big);

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.Equal(_big, r.DocumentId));
    }
}
