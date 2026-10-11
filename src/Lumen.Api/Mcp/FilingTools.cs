namespace Lumen.Api.Mcp;

[McpServerToolType]
public class FilingTools
{
    private readonly IMediator _mediator;

    public FilingTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool(Name = McpConstants.ListFilingsTool, Title = "List filings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists the SEC filings (10-K annual reports, 10-Q quarterly reports) uploaded for the caller's company, " +
                 "one per line with its filing date and documentId. Use a documentId to restrict search_filings to one filing.")]
    public async Task<string> ListFilingsAsync(CancellationToken cancellationToken)
    {
        var documents = await _mediator.Send(new GetDocumentsQuery(), cancellationToken);
        return McpResultFormatter.FormatFilings(documents);
    }

    [McpServerTool(Name = McpConstants.SearchFilingsTool, Title = "Search filings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Semantic search over the caller's company SEC filings. Returns the most relevant passages, best first, as numbered blocks: " +
                 "a heading with the filing and section, the documentId and chunkId, and the passage text between <<<chunk and chunk>>>. " +
                 "A passage is about 250 tokens of prose, or rows of a table written as 'label | value | value'; large tables are split by rows. " +
                 "Amounts are usually in millions of US dollars, as stated in the filing text. " +
                 "If the passages do not answer the question, call again with a rephrased or more specific query.")]
    public async Task<string> SearchFilingsAsync(
        [Description("What to look for, in natural language, e.g. 'total net sales fiscal 2025' or 'CET1 capital ratio'.")] string query,
        [Description("How many passages to return, 1-20. Default 10.")] int topK = McpConstants.DefaultTopK,
        [Description("Optional documentId from list_filings to search only that filing.")] string? documentId = null,
        CancellationToken cancellationToken = default)
    {
        if (topK is < 1 or > McpConstants.MaxTopK)
        {
            throw new ValidationException($"topK must be between 1 and {McpConstants.MaxTopK}.");
        }

        Guid? filter = null;
        if (!string.IsNullOrWhiteSpace(documentId))
        {
            if (!Guid.TryParse(documentId, out var parsed))
            {
                throw new ValidationException("documentId must be a documentId returned by list_filings.");
            }

            filter = parsed;
        }

        var results = await _mediator.Send(new SearchChunksQuery(query, topK, filter), cancellationToken);
        return McpResultFormatter.FormatSearchResults(results);
    }
}
