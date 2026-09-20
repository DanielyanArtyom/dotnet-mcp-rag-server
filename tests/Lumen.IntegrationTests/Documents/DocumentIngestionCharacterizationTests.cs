using Xunit.Abstractions;

namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentIngestionCharacterizationTests(PostgresFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Characterization", Role.Admin);

        _client = _factory.CreateClient();
        _client.Timeout = TimeSpan.FromMinutes(5);
        await TestAuth.AuthenticateAsync(_client, email, password);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_real_Apple_10K_ingests_without_error_and_respects_the_token_budget()
    {
        var fileBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"));
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "apple-10k-fy2025.htm");
        content.Add(new StringContent("Apple 10-K FY2025"), "title");
        content.Add(new StringContent("2025-10-31"), "filingDate");

        var stopwatch = Stopwatch.StartNew();
        var uploadResponse = await _client.PostAsync("/api/documents/upload", content);
        stopwatch.Stop();
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentDto>();

        var chunksResponse = await _client.GetAsync($"/api/documents/{uploaded!.Id}/chunks");
        Assert.Equal(HttpStatusCode.OK, chunksResponse.StatusCode);
        var chunks = await chunksResponse.Content.ReadFromJsonAsync<List<DocumentChunkDto>>();

        var tokens = chunks!.Select(c => c.TokenCount).Order().ToList();
        var tableCount = chunks!.Count(c => c.ChunkType == "Table");
        var proseCount = chunks!.Count(c => c.ChunkType == "Prose");
        output.WriteLine($"Upload elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
        output.WriteLine($"Document content chars: {uploaded.Content.Length}");
        output.WriteLine($"Chunks: {chunks!.Count} (prose {proseCount}, table {tableCount})");
        output.WriteLine($"Tokens min/median/p95/max: {tokens[0]}/{tokens[tokens.Count / 2]}/{tokens[(int)(tokens.Count * 0.95)]}/{tokens[^1]}");

        var allText = string.Concat(chunks!.Select(c => c.Text));
        output.WriteLine($"Chunk text / document content length ratio: {(double)allText.Length / uploaded.Content.Length:F3}");

        Assert.Contains("Apple Inc.", allText);
        Assert.Contains("iPhone", allText);
        Assert.Contains("Total net sales", allText);
        Assert.Contains(chunks!, c => c.ChunkType == "Table" && c.Text.Contains("Total gross margin"));
        Assert.True(chunks!.Count > 50);
        Assert.All(chunks!, c => Assert.True(c.TokenCount <= 250, $"Chunk {c.ChunkIndex} has {c.TokenCount} tokens, exceeding MaxTokensPerChunk"));
        Assert.Contains(chunks!, c => c.ChunkType == "Table");
        Assert.Contains(chunks!, c => c.ChunkType == "Prose");
        Assert.Equal(Enumerable.Range(0, chunks!.Count), chunks!.Select(c => c.ChunkIndex));
        Assert.All(chunks!, c => Assert.False(string.IsNullOrWhiteSpace(c.Text), $"Chunk {c.ChunkIndex} is empty"));
    }
}
