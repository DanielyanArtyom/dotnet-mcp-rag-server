namespace Lumen.IntegrationTests.Ask;

[Collection("Postgres collection")]
public class AskEndpointTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private (string Email, string Password) _admin;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Ask tenant", Role.Admin);
        _admin = (email, password);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task UploadAsync(HttpClient client, string title, string body)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Answers_with_sources_usage_and_timings()
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _admin.Email, _admin.Password);
        await UploadAsync(client, "Apple 10-K", "<p>Total net sales were 416,161 million dollars.</p>");

        var response = await client.PostAsJsonAsync("/api/ask", new { question = "What were total net sales?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AskResponseDto>();
        Assert.False(body!.Refused);
        Assert.False(body.Truncated);
        Assert.NotEmpty(body.Sources);
        Assert.Equal(1, body.Sources[0].SourceId);
        Assert.True(body.Sources[0].Cited);
        Assert.Contains("[1]", body.Answer);
        Assert.True(body.Usage.InputTokens > 0);
        Assert.True(body.Timings.TotalMs >= body.Timings.RetrievalMs);
    }

    [Fact]
    public async Task Validates_the_question()
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, _admin.Email, _admin.Password);

        var blank = await client.PostAsJsonAsync("/api/ask", new { question = "   " });
        var tooLong = await client.PostAsJsonAsync("/api/ask", new { question = new string('a', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Requires_authentication_and_allows_members()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/ask", new { question = "q" })).StatusCode);

        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Ask member tenant", Role.Member);
        using var member = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(member, email, password);

        var response = await member.PostAsJsonAsync("/api/ask", new { question = "Anything at all?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AskResponseDto>();
        Assert.True(body!.Refused);
        Assert.Empty(body.Sources);
    }
}
