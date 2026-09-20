namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentDeleteTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _tenantId;
    private HttpClient _admin = null!;
    private HttpClient _otherTenantAdmin = null!;
    private HttpClient _member = null!;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);

        var (tenantId, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Delete test tenant", Role.Admin);
        _tenantId = tenantId;
        _admin = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_admin, email, password);

        var (_, otherEmail, otherPassword) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Other delete tenant", Role.Admin);
        _otherTenantAdmin = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_otherTenantAdmin, otherEmail, otherPassword);

        var (_, memberEmail, memberPassword) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Member delete tenant", Role.Member);
        _member = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_member, memberEmail, memberPassword);
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        _otherTenantAdmin.Dispose();
        _member.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> UploadAsync()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("<html><body><h1>Title</h1><p>Some prose about the business.</p></body></html>"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        form.Add(file, "file", "report.htm");
        form.Add(new StringContent("Report"), "title");
        form.Add(new StringContent("2025-10-31"), "filingDate");

        var response = await _admin.PostAsync("/api/documents/upload", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!.Id;
    }

    private string RawFilePath(Guid documentId)
    {
        var root = _factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;
        return Path.Combine(root, $"Delete-test-tenant_{_tenantId}", $"Report_{documentId}.htm");
    }

    [Fact]
    public async Task Deleting_a_document_removes_the_row_its_chunks_and_the_raw_file()
    {
        var id = await UploadAsync();
        Assert.True(File.Exists(RawFilePath(id)));

        var response = await _admin.DeleteAsync($"/api/documents/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/documents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/documents/{id}/chunks")).StatusCode);
        Assert.False(File.Exists(RawFilePath(id)));

        await using var context = TestDbContextFactory.Create(fixture.ConnectionString, _tenantId);
        Assert.Empty(await context.DocumentChunks.Where(c => c.DocumentId == id).ToListAsync());
    }

    [Fact]
    public async Task Another_tenants_admin_gets_404_and_the_document_survives()
    {
        var id = await UploadAsync();

        var response = await _otherTenantAdmin.DeleteAsync($"/api/documents/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/api/documents/{id}")).StatusCode);
        Assert.True(File.Exists(RawFilePath(id)));
    }

    [Fact]
    public async Task Deleting_an_unknown_document_returns_404()
    {
        var response = await _admin.DeleteAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_member_gets_403_and_an_anonymous_caller_gets_401()
    {
        var id = await UploadAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await _member.DeleteAsync($"/api/documents/{id}")).StatusCode);

        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/documents/{id}")).StatusCode);

        Assert.True(File.Exists(RawFilePath(id)));
    }
}
