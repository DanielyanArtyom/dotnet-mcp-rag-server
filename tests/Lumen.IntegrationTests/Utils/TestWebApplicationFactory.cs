namespace Lumen.IntegrationTests.Utils;

public static class TestWebApplicationFactory
{
    public static WebApplicationFactory<Program> Create(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("DocumentStorage:RootPath", Path.Combine(Path.GetTempPath(), "lumen-tests", Guid.NewGuid().ToString("N")));
            // Keeps a developer's local Demo:FilingsPath out of the suite.
            builder.UseSetting("Demo:FilingsPath", "");
            // WebApplicationFactory runs as Development, so a developer's local appsettings.Development.json
            // (Azure embeddings/chat) would otherwise reach the suite and make it call Azure.
            builder.UseSetting("Embeddings:Provider", "Fake");
            builder.UseSetting("Chat:Provider", "Fake");
            builder.UseSetting("Chat:SourceContextHeader", "false");
            // Fake embeddings match only byte-identical text; a context header in the embedding input would break
            // every test that searches for a chunk by its own text. Header, decimal and table behaviour have dedicated tests.
            builder.UseSetting("Chunking:ContextHeaders", "false");
            builder.UseSetting("Chunking:ProtectDecimals", "false");
            builder.UseSetting("Chunking:TableCells", "false");
            builder.UseSetting("Chunking:TableHeaders", "false");
            builder.UseSetting("Chunking:TableCaptions", "false");
            builder.UseSetting("Chunking:TableFormat", "Pipe");
        });
}
