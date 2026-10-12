namespace Lumen.IntegrationTests.Settings;

[Collection("Postgres collection")]
public class StartupFailureTests(PostgresFixture fixture)
{
    [Fact]
    public void An_empty_jwt_secret_stops_startup_and_names_the_setting()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("Jwt:Secret", ""));

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("Jwt:Secret", error!.ToString());
    }

    [Fact]
    public void A_missing_connection_string_stops_startup_and_names_the_setting()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", ""));

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("ConnectionStrings:Default", error!.ToString());
    }
}
