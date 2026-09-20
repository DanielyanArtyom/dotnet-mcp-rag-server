namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentUploadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (tenantId, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Upload test tenant", Role.Admin);
        _tenantId = tenantId;

        _client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_client, email, password);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static MultipartFormDataContent BuildForm(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        content.Add(new StringContent("Test Document"), "title");
        content.Add(new StringContent("2025-10-31"), "filingDate");
        return content;
    }

    [Fact]
    public async Task Upload_then_get_chunks_round_trips_a_small_html_document()
    {
        using var form = BuildForm("<html><body><h1>Title</h1><p>Some prose about the business.</p></body></html>"u8.ToArray(), "test.htm", "text/html");

        var uploadResponse = await _client.PostAsync("/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentDto>();
        Assert.NotNull(uploaded);
        Assert.True(uploaded!.ChunkCount > 0);

        var chunksResponse = await _client.GetAsync($"/api/documents/{uploaded.Id}/chunks");
        Assert.Equal(HttpStatusCode.OK, chunksResponse.StatusCode);
        var chunks = await chunksResponse.Content.ReadFromJsonAsync<List<DocumentChunkDto>>();
        Assert.NotEmpty(chunks!);
        Assert.Equal(chunks!.OrderBy(c => c.ChunkIndex).Select(c => c.ChunkIndex), chunks!.Select(c => c.ChunkIndex));

        var root = _factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;
        Assert.True(File.Exists(Path.Combine(root, $"Upload-test-tenant_{_tenantId}", $"Test-Document_{uploaded.Id}.htm")));
    }

    [Theory]
    [InlineData("broken.pdf", "application/pdf")]
    [InlineData("notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task Uploading_an_unreadable_or_unsupported_file_returns_400(string fileName, string contentType)
    {
        using var form = BuildForm("not really a document"u8.ToArray(), fileName, contentType);

        var response = await _client.PostAsync("/api/documents/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Getting_chunks_of_an_unknown_document_returns_404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}/chunks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_as_a_Member_returns_403_and_writes_nothing_to_disk()
    {
        var (memberTenantId, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Member tenant", Role.Member);
        using var memberClient = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(memberClient, email, password);
        using var form = BuildForm("<html><body><p>x</p></body></html>"u8.ToArray(), "test.htm", "text/html");

        var response = await memberClient.PostAsync("/api/documents/upload", form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var root = _factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;
        Assert.True(!Directory.Exists(root) || Directory.GetDirectories(root, $"*_{memberTenantId}").Length == 0);
    }

    [Fact]
    public async Task Uploading_without_authentication_returns_401()
    {
        using var anonymous = _factory.CreateClient();
        using var form = BuildForm("<html></html>"u8.ToArray(), "test.htm", "text/html");

        var response = await anonymous.PostAsync("/api/documents/upload", form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "2025-10-31")]
    [InlineData("Test Document", null)]
    [InlineData("Test Document", "not-a-date")]
    public async Task Uploading_with_a_missing_or_malformed_form_value_returns_400(string? title, string? filingDate)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("<html><body><p>x</p></body></html>"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        form.Add(file, "file", "test.htm");
        if (title is not null) form.Add(new StringContent(title), "title");
        if (filingDate is not null) form.Add(new StringContent(filingDate), "filingDate");

        var response = await _client.PostAsync("/api/documents/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_with_a_title_over_500_characters_returns_400()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("<html><body><p>x</p></body></html>"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        form.Add(file, "file", "test.htm");
        form.Add(new StringContent(new string('x', 501)), "title");
        form.Add(new StringContent("2025-10-31"), "filingDate");

        var response = await _client.PostAsync("/api/documents/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_without_a_file_part_returns_400()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Test Document"), "title");
        form.Add(new StringContent("2025-10-31"), "filingDate");

        var response = await _client.PostAsync("/api/documents/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Content_is_left_out_of_upload_and_list_responses_unless_requested()
    {
        using var form = BuildForm("<html><body><p>Hello content.</p></body></html>"u8.ToArray(), "test.htm", "text/html");
        var uploaded = await (await _client.PostAsync("/api/documents/upload", form)).Content.ReadFromJsonAsync<DocumentDto>();
        Assert.Null(uploaded!.Content);

        var listWithout = await _client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");
        Assert.All(listWithout!, d => Assert.Null(d.Content));

        var listWith = await _client.GetFromJsonAsync<List<DocumentDto>>("/api/documents?includeContent=true");
        Assert.Contains("Hello content.", listWith!.Single(d => d.Id == uploaded.Id).Content);

        var withoutById = await _client.GetFromJsonAsync<DocumentDto>($"/api/documents/{uploaded.Id}");
        Assert.Null(withoutById!.Content);

        var withById = await _client.GetFromJsonAsync<DocumentDto>($"/api/documents/{uploaded.Id}?includeContent=true");
        Assert.Contains("Hello content.", withById!.Content);
    }
}
