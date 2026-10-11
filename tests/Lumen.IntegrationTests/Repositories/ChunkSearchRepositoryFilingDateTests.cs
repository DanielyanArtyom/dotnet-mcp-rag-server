namespace Lumen.IntegrationTests.Repositories;

[Collection("Postgres collection")]
public class ChunkSearchRepositoryFilingDateTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Search_results_carry_the_documents_filing_date()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (tenantId, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Filing date tenant", Role.Admin);
        using var client = factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><body><p>Net sales rose.</p></body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent("Dated filing"), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenant>().TenantId = tenantId;
        var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var repository = scope.ServiceProvider.GetRequiredService<IChunkSearchRepository>();

        var vector = (await embeddings.EmbedAsync(["Net sales rose."]))[0];
        var results = await repository.SearchAsync(vector, "Net sales rose.", embeddings.ModelName, 5, null);

        Assert.NotEmpty(results);
        Assert.Equal(new DateOnly(2025, 9, 27), results[0].DocumentFilingDate);
    }
}
