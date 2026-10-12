using Lumen.Api.Seeding;

namespace Lumen.UnitTests.Seeding;

public class DemoManifestTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lumen-manifest-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteManifest(string json) => File.WriteAllText(Path.Combine(_folder, "manifest.json"), json);

    [Fact]
    public void Loads_entries_with_their_dates()
    {
        File.WriteAllText(Path.Combine(_folder, "a.html"), "<html></html>");
        WriteManifest("""{ "filings": [ { "tenant": "Acme Corp", "title": "Apple 10-K", "filingDate": "2025-09-27", "file": "a.html" } ] }""");

        var filing = Assert.Single(DemoManifest.Load(_folder));

        Assert.Equal(new DemoFiling("Acme Corp", "Apple 10-K", new DateOnly(2025, 9, 27), "a.html"), filing);
    }

    [Fact]
    public void A_folder_without_a_manifest_is_an_error_that_names_the_folder() =>
        Assert.Contains(_folder, Assert.Throws<InvalidOperationException>(() => DemoManifest.Load(_folder)).Message);

    [Fact]
    public void A_missing_file_is_an_error_that_names_it()
    {
        WriteManifest("""{ "filings": [ { "tenant": "Acme Corp", "title": "Apple 10-K", "filingDate": "2025-09-27", "file": "missing.html" } ] }""");

        Assert.Contains("missing.html", Assert.Throws<InvalidOperationException>(() => DemoManifest.Load(_folder)).Message);
    }

    [Fact]
    public void A_missing_filing_date_is_an_error_that_names_the_file_and_title()
    {
        File.WriteAllText(Path.Combine(_folder, "a.html"), "<html></html>");
        WriteManifest("""{ "filings": [ { "tenant": "Acme Corp", "title": "Apple 10-K", "file": "a.html" } ] }""");

        var message = Assert.Throws<InvalidOperationException>(() => DemoManifest.Load(_folder)).Message;

        Assert.Contains("manifest.json", message);
        Assert.Contains("Apple 10-K", message);
    }

    [Fact]
    public void An_empty_manifest_is_an_error()
    {
        WriteManifest("""{ "filings": [] }""");

        Assert.Throws<InvalidOperationException>(() => DemoManifest.Load(_folder));
    }

    [Fact]
    public void The_committed_manifest_lists_the_four_filings_and_every_file_exists()
    {
        var repoSamples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/edgar"));

        var filings = DemoManifest.Load(repoSamples);

        Assert.Equal(["Apple 10-K", "Apple 10-Q", "Morgan 10-K", "Morgan 10-Q"], filings.Select(f => f.Title));
    }
}
