namespace Lumen.Application.Documents.Commands;

// Attributes target the primary-constructor parameters, not [property: ...] — the latter makes
// MVC's ValidationVisitor throw and turns every POST /documents into a 500.
public record CreateDocumentCommand(
    [Required, MaxLength(500)]
    string Title,
    [Required]
    string Content) : IRequest<Document>;

public class CreateDocumentCommandHandler : IRequestHandler<CreateDocumentCommand, Document>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateDocumentCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Document> Handle(CreateDocumentCommand request, CancellationToken ct)
    {
        var document = new Document
        {
            // EF Core would generate this on its own at SaveChanges time, but only on the
            // *entity*, not on this domain object — the generated value never propagates back
            // here, so the response would carry an empty Id if we relied on that instead.
            Id = Guid.NewGuid(),
            Title = request.Title,
            Content = request.Content
        };

        _unitOfWork.Documents.Add(document);
        await _unitOfWork.CompleteAsync(ct);

        return document;
    }
}
