namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentIngestProfileTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Uploaded_documents_and_settings_report_the_same_profile()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Profile tenant", Role.Admin);
        using var client = factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><body><p>Net sales rose.</p></body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent("Profiled"), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();

        var documents = JsonDocument.Parse(await client.GetStringAsync("/api/documents")).RootElement;
        var settings = JsonDocument.Parse(await client.GetStringAsync("/api/settings")).RootElement;

        Assert.Equal("chunk=250/60;hdr=off;merge=0;dec=off", settings.GetProperty("ingestProfile").GetString());
        Assert.All(documents.EnumerateArray(), d => Assert.Equal(settings.GetProperty("ingestProfile").GetString(), d.GetProperty("ingestProfile").GetString()));
    }
}
