using System.Globalization;

namespace Lumen.Infrastructure.Chunking;

public static class ContextHeaderBuilder
{
    public static string Build(ChunkingContext context, string? section)
    {
        var header = $"{context.DocumentTitle} (filed {context.FilingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})";
        return section is null ? header : $"{header} — {section}";
    }
}
