using System.Text.Json;

namespace Lumen.Api.Seeding;

public static class DemoManifest
{
    public const string FileName = "manifest.json";

    private sealed record ManifestFile(List<DemoFiling>? Filings);

    public static IReadOnlyList<DemoFiling> Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Demo:FilingsPath '{folder}' has no {FileName}.");
        }

        var filings = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(path), JsonSerializerOptions.Web)?.Filings ?? [];
        if (filings.Count == 0)
        {
            throw new InvalidOperationException($"{path} lists no filings.");
        }

        foreach (var filing in filings)
        {
            if (string.IsNullOrWhiteSpace(filing.Tenant) || string.IsNullOrWhiteSpace(filing.Title) || string.IsNullOrWhiteSpace(filing.File))
            {
                throw new InvalidOperationException($"{path}: every filing needs tenant, title and file.");
            }

            if (filing.FilingDate == default)
            {
                throw new InvalidOperationException($"{path}: filing '{filing.Title}' has no filingDate.");
            }

            if (!File.Exists(Path.Combine(folder, filing.File)))
            {
                throw new InvalidOperationException($"{path}: file '{filing.File}' for '{filing.Title}' does not exist.");
            }
        }

        return filings;
    }
}
