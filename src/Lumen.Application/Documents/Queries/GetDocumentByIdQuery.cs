namespace Lumen.Application.Documents.Queries;

public record GetDocumentByIdQuery(Guid Id) : IRequest<Document?>;

public class GetDocumentByIdQueryHandler : IRequestHandler<GetDocumentByIdQuery, Document?>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDocumentByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Document?> Handle(GetDocumentByIdQuery request, CancellationToken ct)
        => _unitOfWork.Documents.GetByIdAsync(request.Id, ct);
}
