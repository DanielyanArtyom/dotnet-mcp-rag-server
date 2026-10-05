namespace Lumen.IntegrationTests.Utils;

public static class TestWebApplicationFactory
{
    public static WebApplicationFactory<Program> Create(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("DocumentStorage:RootPath", Path.Combine(Path.GetTempPath(), "lumen-tests", Guid.NewGuid().ToString("N")));
            // WebApplicationFactory runs as Development, so a developer's local appsettings.Development.json
            // (Azure embeddings/chat) would otherwise reach the suite and make it call Azure.
            builder.UseSetting("Embeddings:Provider", "Fake");
            builder.UseSetting("Chat:Provider", "Fake");
        });
}
