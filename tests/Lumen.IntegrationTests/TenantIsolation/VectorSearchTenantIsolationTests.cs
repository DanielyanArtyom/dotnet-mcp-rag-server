using Npgsql;

namespace Lumen.IntegrationTests.TenantIsolation;

[Collection("Postgres collection")]
public class VectorSearchTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string SharedText = "Total net sales increased 2 percent during fiscal 2025.";

    private WebApplicationFactory<Program> _factory = null!;
    private (Guid TenantId, string Email, string Password) _tenantA;
    private (Guid TenantId, string Email, string Password) _tenantB;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _tenantA = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Vector isolation A", Role.Admin);
        _tenantB = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Vector isolation B", Role.Admin);
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
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body><p>{SharedText}</p></body></html>"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");

        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<DocumentDto>();
        return document!.Id;
    }

    [Fact]
    public async Task Search_never_returns_another_tenants_chunks_even_for_identical_text()
    {
        var documentA = await UploadAsync(_tenantA, "Tenant A filing");
        var documentB = await UploadAsync(_tenantB, "Tenant B filing");

        using var clientB = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientB, _tenantB.Email, _tenantB.Password);

        var results = await (await clientB.PostAsJsonAsync("/api/search", new { query = SharedText, topK = 50 }))
            .Content.ReadFromJsonAsync<List<SearchResultDto>>();

        Assert.NotEmpty(results!);
        Assert.All(results!, r => Assert.Equal(documentB, r.DocumentId));
        Assert.DoesNotContain(results!, r => r.DocumentId == documentA);
    }

    // Negative control: DocumentChunks carries no tenant isolation of its own (no row-level
    // security) — a bare, unscoped query over the table leaks across tenants. This is a simpler
    // probe than ChunkSearchRepository's real query (which also joins Documents on its own
    // TenantId condition), not that exact query with only its WHERE clause removed; without this
    // control, the positive test above would prove nothing about the predicate doing real work.
    [Fact]
    public async Task The_same_query_without_the_tenant_predicate_leaks_across_tenants()
    {
        await UploadAsync(_tenantA, "Tenant A filing");
        await UploadAsync(_tenantB, "Tenant B filing");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT COUNT(DISTINCT c."TenantId")::int
            FROM "DocumentChunks" c
            WHERE c."Text" = @text
            """, connection);
        command.Parameters.AddWithValue("text", SharedText);

        var distinctTenants = (int)(await command.ExecuteScalarAsync())!;

        Assert.True(distinctTenants >= 2, "both tenants must hold a chunk with the same text for the isolation test above to mean anything");
    }
}
