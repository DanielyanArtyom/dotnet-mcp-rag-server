namespace Lumen.IntegrationTests.Search;

[Collection("Postgres collection")]
public class VectorSearchTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private (string Email, string Password) _admin;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Search tenant", Role.Admin);
        _admin = (email, password);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string title, string body)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");

        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<DocumentDto>();
        return document!.Id;
    }

    // Each paragraph is uploaded as its own document (rather than three <p>s in one document) so the
    // 250-token default chunk budget doesn't pack all three short sentences into a single chunk —
    // that would leave no chunk whose text exactly equals the query, and the FakeEmbeddingService only
    // produces distance 0 for byte-identical text.
    [Fact]
    public async Task Searching_for_a_chunks_exact_text_ranks_that_chunk_first()
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _admin.Email, _admin.Password);
        await UploadAsync(client, "Other filing 1", "<p>Total net sales increased 2 percent.</p>");
        await UploadAsync(client, "Filing", "<p>Gross margin percentage decreased.</p>");
        await UploadAsync(client, "Other filing 2", "<p>Segment operating performance was mixed.</p>");

        var response = await client.PostAsJsonAsync("/api/search", new { query = "Gross margin percentage decreased.", topK = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<SearchResultDto>>();
        Assert.NotEmpty(results!);
        Assert.Contains("Gross margin percentage decreased.", results![0].Text);
        Assert.Equal(0d, results[0].Distance, precision: 5);
        Assert.Equal("Filing", results[0].DocumentTitle);
    }

    [Fact]
    public async Task TopK_limits_the_number_of_results()
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _admin.Email, _admin.Password);
        await UploadAsync(client, "Filing", "<p>One.</p><p>Two.</p><p>Three.</p><p>Four.</p>");

        var results = await (await client.PostAsJsonAsync("/api/search", new { query = "One.", topK = 2 }))
            .Content.ReadFromJsonAsync<List<SearchResultDto>>();

        Assert.True(results!.Count <= 2);
    }

    [Fact]
    public async Task DocumentId_restricts_the_search_to_one_document()
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _admin.Email, _admin.Password);
        var first = await UploadAsync(client, "First filing", "<p>Total net sales increased.</p>");
        await UploadAsync(client, "Second filing", "<p>Total net sales increased.</p>");

        var results = await (await client.PostAsJsonAsync("/api/search", new { query = "Total net sales increased.", topK = 10, documentId = first }))
            .Content.ReadFromJsonAsync<List<SearchResultDto>>();

        Assert.NotEmpty(results!);
        Assert.All(results!, r => Assert.Equal(first, r.DocumentId));
    }

    [Fact]
    public async Task Rejects_a_blank_query_and_an_out_of_range_topK()
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _admin.Email, _admin.Password);

        var blank = await client.PostAsJsonAsync("/api/search", new { query = "   ", topK = 5 });
        var tooMany = await client.PostAsJsonAsync("/api/search", new { query = "net sales", topK = 500 });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Requires_authentication_and_allows_members()
    {
        using var anonymous = _factory.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/search", new { query = "net sales", topK = 5 });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var (_, memberEmail, memberPassword) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Member tenant", Role.Member);
        using var member = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(member, memberEmail, memberPassword);

        var memberResponse = await member.PostAsJsonAsync("/api/search", new { query = "net sales", topK = 5 });
        Assert.Equal(HttpStatusCode.OK, memberResponse.StatusCode);
    }
}
