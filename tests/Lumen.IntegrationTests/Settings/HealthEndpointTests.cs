namespace Lumen.IntegrationTests.Settings;

[Collection("Postgres collection")]
public class HealthEndpointTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer_anonymously(string path)
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_is_503_and_live_is_200_when_the_database_is_unreachable()
    {
        using var factory = TestWebApplicationFactory.Create("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2")
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Database:MigrateOnStartup", "false");
                b.UseSetting("Database:SeedDemoData", "false");
            });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
