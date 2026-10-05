namespace Lumen.IntegrationTests.TenantIsolation;

[Collection("Postgres collection")]
public class AskTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string SharedText = "Total net sales increased 2 percent during fiscal 2025.";

    private WebApplicationFactory<Program> _factory = null!;
    private (Guid TenantId, string Email, string Password) _tenantA;
    private (Guid TenantId, string Email, string Password) _tenantB;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _tenantA = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Ask isolation A", Role.Admin);
        _tenantB = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Ask isolation B", Role.Admin);
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

    [Fact]
    public async Task Ask_never_sends_another_tenants_chunks_to_the_model_even_for_identical_text()
    {
        var documentA = await UploadAsync(_tenantA, "Tenant A filing");
        var documentB = await UploadAsync(_tenantB, "Tenant B filing");

        using var clientB = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientB, _tenantB.Email, _tenantB.Password);

        var body = await (await clientB.PostAsJsonAsync("/api/ask", new { question = SharedText }))
            .Content.ReadFromJsonAsync<AskResponseDto>();

        Assert.NotEmpty(body!.Sources);
        Assert.All(body.Sources, s => Assert.Equal(documentB, s.DocumentId));
        Assert.DoesNotContain(body.Sources, s => s.DocumentId == documentA);
    }
}
