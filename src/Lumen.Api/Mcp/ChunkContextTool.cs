namespace Lumen.Api.Mcp;

[McpServerToolType]
public class ChunkContextTool
{
    private readonly IMediator _mediator;

    public ChunkContextTool(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool(Name = McpConstants.ChunkContextTool, Title = "Get surrounding passages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns a passage from search_filings together with the passages just before and after it in the same filing, in document order. " +
                 "Use it when a table is cut off (its header row or caption is in an earlier passage) or a sentence continues across passages. " +
                 "The requested passage is marked (requested).")]
    public async Task<string> GetChunkContextAsync(
        [Description("chunkId of a passage returned by search_filings.")] string chunkId,
        [Description("Passages to include before it, 0-3. Default 1.")] int before = 1,
        [Description("Passages to include after it, 0-3. Default 1.")] int after = 1,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(chunkId, out var id))
        {
            throw new ValidationException("chunkId must be a chunkId returned by search_filings.");
        }

        var context = await _mediator.Send(new GetChunkContextQuery(id, before, after), cancellationToken);
        return McpResultFormatter.FormatChunkContext(context);
    }
}
