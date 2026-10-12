namespace Lumen.IntegrationTests.Settings;

[Collection("Postgres collection")]
public class StartupFlagsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Swagger_is_served_in_Development_by_default()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task Swagger_can_be_switched_off()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("Swagger:Enabled", "false"));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task Users_are_seeded_when_SeedDemoData_is_on()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { email = "admin@acme.test", password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
