namespace Lumen.Application.Documents.Queries;

public record GetChunkContextQuery(Guid ChunkId, int Before, int After) : IRequest<ChunkContext>;

public class GetChunkContextQueryHandler : IRequestHandler<GetChunkContextQuery, ChunkContext>
{
    public const int MaxWindow = 3;

    private readonly IUnitOfWork _unitOfWork;

    public GetChunkContextQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ChunkContext> Handle(GetChunkContextQuery request, CancellationToken ct)
    {
        if (request.Before is < 0 or > MaxWindow || request.After is < 0 or > MaxWindow)
        {
            throw new Exceptions.ValidationException($"before and after must be between 0 and {MaxWindow}.");
        }

        var chunk = await _unitOfWork.Chunks.GetByIdAsync(request.ChunkId, ct)
                    ?? throw new NotFoundException($"Chunk {request.ChunkId} was not found.");
        var document = await _unitOfWork.Documents.GetByIdAsync(chunk.DocumentId, ct)
                       ?? throw new NotFoundException($"Document {chunk.DocumentId} was not found.");
        var chunks = await _unitOfWork.Chunks.GetByIndexRangeAsync(
            chunk.DocumentId, Math.Max(0, chunk.ChunkIndex - request.Before), chunk.ChunkIndex + request.After, ct);

        return new ChunkContext
        {
            DocumentId = document.Id,
            DocumentTitle = document.Title,
            DocumentFilingDate = document.FilingDate,
            RequestedChunkId = chunk.Id,
            Chunks = chunks
        };
    }
}
