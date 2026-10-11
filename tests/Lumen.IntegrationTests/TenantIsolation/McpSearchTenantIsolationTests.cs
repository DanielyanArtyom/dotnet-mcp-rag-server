namespace Lumen.IntegrationTests.TenantIsolation;

[Collection("Postgres collection")]
public class McpSearchTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string SharedText = "Total net sales increased 2 percent during fiscal 2025.";

    private WebApplicationFactory<Program> _factory = null!;
    private (Guid TenantId, string Email, string Password) _tenantA;
    private (Guid TenantId, string Email, string Password) _tenantB;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("Mcp:ContextTool", "true"));
        _tenantA = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Mcp isolation A", Role.Admin);
        _tenantB = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Mcp isolation B", Role.Admin);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> UploadAsync((Guid TenantId, string Email, string Password) tenant, string title)
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, tenant.Email, tenant.Password);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body><p>{SharedText}</p></body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");

        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!.Id;
    }

    private async Task<string> TokenAsync((Guid TenantId, string Email, string Password) tenant)
    {
        using var client = _factory.CreateClient();
        return await TestAuth.LoginAsync(client, tenant.Email, tenant.Password);
    }

    private static List<Guid> Ids(string text, string label) =>
        Regex.Matches(text, label + @": ([0-9a-fA-F-]{36})").Select(m => Guid.Parse(m.Groups[1].Value)).ToList();

    [Fact]
    public async Task Mcp_search_returns_the_callers_own_chunk_and_never_another_tenants()
    {
        var documentA = await UploadAsync(_tenantA, "Tenant A filing");
        var documentB = await UploadAsync(_tenantB, "Tenant B filing");
        await using var client = await TestMcp.ConnectAsync(_factory, await TokenAsync(_tenantB));

        var result = await client.CallToolAsync("search_filings", new Dictionary<string, object?> { ["query"] = SharedText, ["topK"] = 20 });
        var text = TestMcp.TextOf(result);
        var documentIds = Ids(text, "documentId");

        Assert.NotEqual(true, result.IsError);
        // Positive control: an empty result would also "never leak". It is what a tool running outside the
        // request scope (stateful session mode, tenant = Guid.Empty) returns, so the test must see B's own chunk.
        Assert.Contains(documentB, documentIds);
        Assert.DoesNotContain(documentA, documentIds);
        Assert.Contains("Tenant B filing", text);
        Assert.DoesNotContain("Tenant A filing", text);
    }

    [Fact]
    public async Task Mcp_search_scoped_to_another_tenants_document_finds_nothing()
    {
        var documentA = await UploadAsync(_tenantA, "Tenant A filing");
        var documentB = await UploadAsync(_tenantB, "Tenant B filing");
        await using var client = await TestMcp.ConnectAsync(_factory, await TokenAsync(_tenantB));

        var own = await client.CallToolAsync("search_filings",
            new Dictionary<string, object?> { ["query"] = SharedText, ["documentId"] = documentB.ToString() });
        Assert.Contains(documentB, Ids(TestMcp.TextOf(own), "documentId"));

        var result = await client.CallToolAsync("search_filings",
            new Dictionary<string, object?> { ["query"] = SharedText, ["documentId"] = documentA.ToString() });

        Assert.Equal("No matching passages. Try rephrasing the query or call list_filings to narrow by documentId.", TestMcp.TextOf(result));
    }

    [Fact]
    public async Task Mcp_list_filings_shows_only_the_callers_documents()
    {
        var documentA = await UploadAsync(_tenantA, "Tenant A filing");
        var documentB = await UploadAsync(_tenantB, "Tenant B filing");
        await using var client = await TestMcp.ConnectAsync(_factory, await TokenAsync(_tenantB));

        var ids = Ids(TestMcp.TextOf(await client.CallToolAsync("list_filings", new Dictionary<string, object?>())), "documentId");

        Assert.Equal([documentB], ids);
        Assert.DoesNotContain(documentA, ids);
    }

    [Fact]
    public async Task Mcp_chunk_context_for_another_tenants_chunk_is_not_found_and_leaks_no_text()
    {
        await UploadAsync(_tenantA, "Tenant A filing");
        await using var clientA = await TestMcp.ConnectAsync(_factory, await TokenAsync(_tenantA));
        var chunkIds = Ids(TestMcp.TextOf(await clientA.CallToolAsync("search_filings", new Dictionary<string, object?> { ["query"] = SharedText })), "chunkId");
        Assert.NotEmpty(chunkIds);
        var chunkA = chunkIds[0];

        var ownResult = await clientA.CallToolAsync("get_chunk_context", new Dictionary<string, object?> { ["chunkId"] = chunkA.ToString() });
        Assert.False(ownResult.IsError ?? false);
        Assert.Contains(SharedText, TestMcp.TextOf(ownResult));
        Assert.Contains("(requested)", TestMcp.TextOf(ownResult));

        await using var clientB = await TestMcp.ConnectAsync(_factory, await TokenAsync(_tenantB));
        var result = await clientB.CallToolAsync("get_chunk_context", new Dictionary<string, object?> { ["chunkId"] = chunkA.ToString() });

        Assert.True(result.IsError);
        Assert.Equal("Not found. Use list_filings or search_filings to get valid ids.", TestMcp.TextOf(result));
        Assert.DoesNotContain(SharedText, TestMcp.TextOf(result));
    }
}
