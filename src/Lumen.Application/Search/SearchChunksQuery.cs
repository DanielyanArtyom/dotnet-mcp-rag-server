namespace Lumen.Application.Search;

public record SearchChunksQuery(string Query, int TopK, Guid? DocumentId) : IRequest<List<ChunkSearchResult>>;

public class SearchChunksQueryHandler : IRequestHandler<SearchChunksQuery, List<ChunkSearchResult>>
{
    private readonly IEmbeddingService _embeddings;
    private readonly IChunkSearchRepository _chunks;

    public SearchChunksQueryHandler(IEmbeddingService embeddings, IChunkSearchRepository chunks)
    {
        _embeddings = embeddings;
        _chunks = chunks;
    }

    public async Task<List<ChunkSearchResult>> Handle(SearchChunksQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new Exceptions.ValidationException("A non-empty query is required.");
        }

        IReadOnlyList<float[]> vectors;
        try
        {
            vectors = await _embeddings.EmbedAsync([request.Query], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalServiceException("Generating an embedding for the search query failed.", ex);
        }

        return await _chunks.SearchAsync(vectors[0], _embeddings.ModelName, request.TopK, request.DocumentId, ct);
    }
}
