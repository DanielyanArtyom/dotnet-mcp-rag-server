namespace Lumen.UnitTests.Chunking;

public static class TableFixtures
{
    public static async Task<DocumentElement> LoadAsync(string name)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tables", name + ".html"));
        return (await new HtmlDocumentReader().ReadAsync(stream)).Single(e => e.Type == ElementType.Table);
    }
}
