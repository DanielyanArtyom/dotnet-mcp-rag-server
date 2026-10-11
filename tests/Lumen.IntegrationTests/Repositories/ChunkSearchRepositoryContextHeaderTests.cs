namespace Lumen.IntegrationTests.Repositories;

[Collection("Postgres collection")]
public class ChunkSearchRepositoryContextHeaderTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("Vector")]
    [InlineData("Hybrid")]
    public async Task Search_results_carry_the_chunks_context_header(string mode)
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString).WithWebHostBuilder(b =>
        {
            b.UseSetting("Chunking:ContextHeaders", "true");
            b.UseSetting("Search:Mode", mode);
        });
        var (tenantId, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, $"Header tenant {mode}", Role.Admin);
        using var client = factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><body><p>Gross Margin</p><p>Products gross margin rose.</p></body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent("Header filing"), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenant>().TenantId = tenantId;
        var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var repository = scope.ServiceProvider.GetRequiredService<IChunkSearchRepository>();

        var vector = (await embeddings.EmbedAsync(["gross margin"]))[0];
        var results = await repository.SearchAsync(vector, "gross margin", embeddings.ModelName, 5, null);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.StartsWith("Header filing (filed 2025-09-27)", r.ContextHeader));
    }
}
