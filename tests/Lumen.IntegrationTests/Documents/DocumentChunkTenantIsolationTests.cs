namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentChunkTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private (string Email, string Password) _tenantA;
    private (string Email, string Password) _tenantB;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, emailA, passwordA) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Chunk isolation A", Role.Admin);
        var (_, emailB, passwordB) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Chunk isolation B", Role.Admin);
        _tenantA = (emailA, passwordA);
        _tenantB = (emailB, passwordB);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("<html><body><p>Tenant-specific content.</p></body></html>"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "doc.htm");
        content.Add(new StringContent("Doc"), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");

        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<DocumentDto>();
        return document!.Id;
    }

    [Fact]
    public async Task Tenant_A_cannot_read_Tenant_Bs_chunks_by_id()
    {
        using var clientB = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientB, _tenantB.Email, _tenantB.Password);
        var tenantBDocumentId = await UploadAsync(clientB);

        var ownResponse = await clientB.GetAsync($"/api/documents/{tenantBDocumentId}/chunks");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        var ownChunks = await ownResponse.Content.ReadFromJsonAsync<List<DocumentChunkDto>>();
        Assert.NotEmpty(ownChunks!);

        using var clientA = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientA, _tenantA.Email, _tenantA.Password);

        var response = await clientA.GetAsync($"/api/documents/{tenantBDocumentId}/chunks");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var listed = await clientA.GetFromJsonAsync<List<DocumentDto>>("/api/documents");
        Assert.DoesNotContain(listed!, d => d.Id == tenantBDocumentId);
    }
}
