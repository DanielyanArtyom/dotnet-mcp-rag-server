namespace Lumen.Application.Documents.Queries;

public record GetDocumentChunksQuery(Guid DocumentId) : IRequest<List<DocumentChunk>>;

public class GetDocumentChunksQueryHandler : IRequestHandler<GetDocumentChunksQuery, List<DocumentChunk>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDocumentChunksQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<List<DocumentChunk>> Handle(GetDocumentChunksQuery request, CancellationToken ct) =>
        _unitOfWork.Chunks.GetByDocumentIdAsync(request.DocumentId, ct);
}
