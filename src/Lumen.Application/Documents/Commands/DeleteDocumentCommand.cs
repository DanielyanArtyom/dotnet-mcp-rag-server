namespace Lumen.Application.Documents.Commands;

public record DeleteDocumentCommand(Guid Id) : IRequest;

public class DeleteDocumentCommandHandler : IRequestHandler<DeleteDocumentCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _fileStorage;

    public DeleteDocumentCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
    }

    public async Task Handle(DeleteDocumentCommand request, CancellationToken ct)
    {
        var document = await _unitOfWork.Documents.GetByIdAsync(request.Id, ct)
                       ?? throw new NotFoundException($"Document {request.Id} was not found.");

        // Chunks go with the document through the database's cascade. The row is removed before the file:
        // a failure in between leaves an orphan file, never a document that points at a missing file.
        _unitOfWork.Documents.Remove(document);
        await _unitOfWork.CompleteAsync(ct);

        await _fileStorage.DeleteAsync(document.RawFilePath, ct);
    }
}
