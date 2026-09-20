namespace Lumen.IntegrationTests.Utils;

public static class TestWebApplicationFactory
{
    public static WebApplicationFactory<Program> Create(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("DocumentStorage:RootPath", Path.Combine(Path.GetTempPath(), "lumen-tests", Guid.NewGuid().ToString("N")));
        });
}
