namespace Lumen.Application.Documents.Queries;

public record GetDocumentsQuery : IRequest<List<Document>>;

public class GetDocumentsQueryHandler : IRequestHandler<GetDocumentsQuery, List<Document>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDocumentsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<List<Document>> Handle(GetDocumentsQuery _, CancellationToken ct)
        => _unitOfWork.Documents.GetAllAsync(ct);
}
