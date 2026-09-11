namespace Lumen.IntegrationTests.TenantIsolation;

[Collection("Postgres collection")]
public class DocumentTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private (Guid TenantId, string Email, string Password, Guid DocumentId) _tenantA;
    private (Guid TenantId, string Email, string Password, Guid DocumentId) _tenantB;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _tenantA = await TestDataSeeder.SeedTenantWithUserAndDocumentAsync(
            fixture.ConnectionString, "Acme", "Acme secret document", "Only Acme should ever see this.");
        _tenantB = await TestDataSeeder.SeedTenantWithUserAndDocumentAsync(
            fixture.ConnectionString, "Globex", "Globex secret document", "Only Globex should ever see this.");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Tenant_A_can_never_see_Tenant_Bs_documents_through_the_API()
    {
        var client = _factory.CreateClient();

        await TestAuth.AuthenticateAsync(client, _tenantA.Email, _tenantA.Password);
        var documentsForA = await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");

        Assert.NotNull(documentsForA);
        Assert.Single(documentsForA);
        Assert.Equal("Acme secret document", documentsForA![0].Title);

        await TestAuth.AuthenticateAsync(client, _tenantB.Email, _tenantB.Password);
        var documentsForB = await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");

        Assert.NotNull(documentsForB);
        Assert.Single(documentsForB);
        Assert.Equal("Globex secret document", documentsForB![0].Title);
    }

    [Fact]
    public async Task Tenant_A_asking_for_Tenant_Bs_document_by_id_gets_404_not_the_document()
    {
        var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _tenantA.Email, _tenantA.Password);

        var response = await client.GetAsync($"/api/documents/{_tenantB.DocumentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Globex secret document", body);
    }

    [Fact]
    public async Task A_document_created_over_HTTP_by_Tenant_A_is_invisible_to_Tenant_B()
    {
        var client = _factory.CreateClient();
        var title = $"Acme quarterly numbers {Guid.NewGuid()}";

        await TestAuth.AuthenticateAsync(client, _tenantA.Email, _tenantA.Password);

        var created = await client.PostAsJsonAsync("/api/documents", new { title, content = "Revenue is up." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var documentsForA = await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");
        Assert.NotNull(documentsForA);
        Assert.Equal(2, documentsForA!.Count);
        Assert.Contains(documentsForA, d => d.Title == title);

        await TestAuth.AuthenticateAsync(client, _tenantB.Email, _tenantB.Password);

        var documentsForB = await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");
        Assert.NotNull(documentsForB);
        Assert.Single(documentsForB);
        Assert.Equal("Globex secret document", documentsForB![0].Title);
        Assert.DoesNotContain(documentsForB, d => d.Title == title);
    }
}
