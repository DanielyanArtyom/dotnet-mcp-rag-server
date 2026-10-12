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
        var withContent = await _client.GetFromJsonAsync<DocumentDto>($"/api/documents/{uploaded.Id}?includeContent=true");
        var contentLength = withContent!.Content!.Length;
        output.WriteLine($"Upload elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
        output.WriteLine($"Document content chars: {contentLength}");
        output.WriteLine($"Chunks: {chunks!.Count} (prose {proseCount}, table {tableCount})");
        output.WriteLine($"Tokens min/median/p95/max: {tokens[0]}/{tokens[tokens.Count / 2]}/{tokens[(int)(tokens.Count * 0.95)]}/{tokens[^1]}");

        var allText = string.Concat(chunks!.Select(c => c.Text));
        output.WriteLine($"Chunk text / document content length ratio: {(double)allText.Length / contentLength:F3}");

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

    [Fact]
    public async Task With_context_headers_on_every_Apple_10K_chunk_carries_a_header_with_its_section()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Chunking:ContextHeaders", "true"));
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "CharacterizationHeaders", Role.Admin);
        await TestAuth.AuthenticateAsync(client, email, password);

        var fileBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"));
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "apple-10k-fy2025.htm");
        content.Add(new StringContent("Apple 10-K FY2025"), "title");
        content.Add(new StringContent("2025-10-31"), "filingDate");

        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentDto>();
        var chunks = await client.GetFromJsonAsync<List<DocumentChunkDto>>($"/api/documents/{uploaded!.Id}/chunks");

        output.WriteLine($"Chunks with headers on: {chunks!.Count}");
        foreach (var header in chunks.Select(c => c.ContextHeader).Distinct().Take(25))
            output.WriteLine($"Header: {header}");

        Assert.All(chunks, c => Assert.StartsWith("Apple 10-K FY2025", c.ContextHeader));
        Assert.Contains(chunks, c => c.ContextHeader!.Contains("— Gross Margin"));
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250));
    }

    [Fact]
    public async Task With_MergeBelowTokens_the_Apple_10K_has_fewer_chunks_and_still_respects_the_budget()
    {
        var baseline = await UploadAppleAsync(_factory, "CharacterizationMergeBase");
        using var mergingFactory = _factory.WithWebHostBuilder(b => b.UseSetting("Chunking:MergeBelowTokens", "20"));
        var merged = await UploadAppleAsync(mergingFactory, "CharacterizationMerge");

        var shortBefore = baseline.Count(c => c.TokenCount <= 20);
        var shortAfter = merged.Count(c => c.TokenCount <= 20);
        output.WriteLine($"Chunks default/merged: {baseline.Count}/{merged.Count}; chunks <= 20 tokens default/merged: {shortBefore}/{shortAfter}");

        Assert.True(merged.Count < baseline.Count);
        Assert.True(shortAfter < shortBefore);
        Assert.All(merged, c => Assert.True(c.TokenCount <= 250));
        Assert.Equal(Enumerable.Range(0, merged.Count), merged.Select(c => c.ChunkIndex));
    }

    [Fact]
    public async Task With_ProtectDecimals_no_Apple_10K_chunk_has_a_broken_decimal_and_the_budget_holds()
    {
        var baseline = await UploadAppleAsync(_factory, "CharacterizationDecimalsBase");
        using var protectingFactory = _factory.WithWebHostBuilder(b => b.UseSetting("Chunking:ProtectDecimals", "true"));
        var protectedChunks = await UploadAppleAsync(protectingFactory, "CharacterizationDecimals");

        var brokenBefore = baseline.Count(c => Regex.IsMatch(c.Text, @"\d\.\s+\d"));
        var brokenAfter = protectedChunks.Count(c => Regex.IsMatch(c.Text, @"\d\.\s+\d"));
        output.WriteLine($"Chunks with a broken decimal default/protected: {brokenBefore}/{brokenAfter} of {baseline.Count}/{protectedChunks.Count}");

        Assert.Equal(0, brokenAfter);
        Assert.All(protectedChunks, c => Assert.DoesNotContain("\u2024", c.Text));
        Assert.All(protectedChunks, c => Assert.True(c.TokenCount <= 250));
    }

    [Fact]
    public async Task With_table_formatting_on_Apple_10K_tables_carry_a_caption_and_aligned_cells_and_the_budget_holds()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Chunking:TableCells", "true");
            b.UseSetting("Chunking:TableHeaders", "true");
            b.UseSetting("Chunking:TableCaptions", "true");
        });
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "CharacterizationTables", Role.Admin);
        await TestAuth.AuthenticateAsync(client, email, password);

        var fileBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"));
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "apple-10k-fy2025.htm");
        content.Add(new StringContent("Apple 10-K FY2025"), "title");
        content.Add(new StringContent("2025-10-31"), "filingDate");

        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentDto>();
        var chunks = (await client.GetFromJsonAsync<List<DocumentChunkDto>>($"/api/documents/{uploaded!.Id}/chunks"))!;
        var document = await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{uploaded.Id}");

        var tableChunks = chunks.Where(c => c.ChunkType == "Table").ToList();
        output.WriteLine($"Chunks with table formatting on: {chunks.Count} (table {tableChunks.Count}); captioned table chunks: {tableChunks.Count(c => c.Text.StartsWith("Table: "))}");

        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250, $"Chunk {c.ChunkIndex} has {c.TokenCount} tokens"));
        Assert.Contains(tableChunks, c => c.Text.StartsWith("Table: "));
        Assert.Contains(tableChunks, c => c.Text.Contains("Research and development | $34,550 | 10% | $31,370"));
        Assert.EndsWith(";cells=v2;thdr=v2;tcap=v2", document!.IngestProfile);
    }

    private async Task<List<DocumentChunkDto>> UploadAppleAsync(WebApplicationFactory<Program> factory, string tenantName)
    {
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, tenantName, Role.Admin);
        await TestAuth.AuthenticateAsync(client, email, password);

        var fileBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"));
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "apple-10k-fy2025.htm");
        content.Add(new StringContent("Apple 10-K FY2025"), "title");
        content.Add(new StringContent("2025-10-31"), "filingDate");

        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentDto>();
        return (await client.GetFromJsonAsync<List<DocumentChunkDto>>($"/api/documents/{uploaded!.Id}/chunks"))!;
    }
}
