namespace Lumen.Api.Mcp;

public static class McpResultFormatter
{
    public static string FormatFilings(IReadOnlyList<Document> documents)
    {
        if (documents.Count == 0)
        {
            return McpConstants.NoFilingsText;
        }

        return string.Join("\n", documents
            .OrderBy(d => d.FilingDate).ThenBy(d => d.Title)
            .Select(d => $"- {d.Title} — filed {d.FilingDate:yyyy-MM-dd} — documentId: {d.Id}"));
    }

    public static string FormatSearchResults(IReadOnlyList<ChunkSearchResult> results)
    {
        if (results.Count == 0)
        {
            return McpConstants.NoResultsText;
        }

        return string.Join("\n\n", results.Select((r, i) => Block(
            i + 1, Heading(r.ContextHeader, r.DocumentTitle, r.DocumentFilingDate), r.DocumentId, r.ChunkId, r.ChunkType, r.Text, requested: false)));
    }

    public static string FormatChunkContext(ChunkContext context) =>
        string.Join("\n\n", context.Chunks.Select((c, i) => Block(
            i + 1, Heading(c.ContextHeader, context.DocumentTitle, context.DocumentFilingDate), c.DocumentId, c.Id, c.ChunkType, c.Text,
            requested: c.Id == context.RequestedChunkId)));

    private static string Heading(string? contextHeader, string title, DateOnly filingDate) =>
        string.IsNullOrWhiteSpace(contextHeader) ? $"{title} (filed {filingDate:yyyy-MM-dd})" : contextHeader;

    private static string Block(int ordinal, string heading, Guid documentId, Guid chunkId, ChunkType type, string text, bool requested) =>
        $"## [{ordinal}] {heading}{(requested ? " (requested)" : string.Empty)}\n" +
        $"documentId: {documentId}  chunkId: {chunkId}  type: {type.ToString().ToLowerInvariant()}\n\n" +
        $"{McpConstants.ChunkOpen}\n{text.Replace(McpConstants.ChunkClose, McpConstants.ChunkCloseEscaped)}\n{McpConstants.ChunkClose}";
}
