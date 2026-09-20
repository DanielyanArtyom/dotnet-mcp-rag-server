namespace Lumen.Application.Documents.Commands;

public record UploadDocumentCommand(
    string FileName,
    Stream Content,
    [Required, MaxLength(500)]
    string Title,
    DateOnly FilingDate) : IRequest<UploadDocumentResult>;

public record UploadDocumentResult(Document Document, int ChunkCount);

public class UploadDocumentCommandHandler : IRequestHandler<UploadDocumentCommand, UploadDocumentResult>
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IChunker _chunker;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;

    public UploadDocumentCommandHandler(
        IDocumentReaderFactory readerFactory,
        IChunker chunker,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentTenant currentTenant)
    {
        _readerFactory = readerFactory;
        _chunker = chunker;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
    }

    public async Task<UploadDocumentResult> Handle(UploadDocumentCommand request, CancellationToken ct)
    {
        var reader = GetReader(request.FileName);
        var bytes = await BufferAsync(request.Content, ct);
        var elements = await ReadElementsAsync(reader, request.FileName, bytes, ct);

        var documentId = Guid.NewGuid();
        var chunks = _chunker.Chunk(documentId, elements);
        var rawFilePath = await SaveRawFileAsync(request.FileName, documentId, bytes, ct);

        var document = BuildDocument(request, documentId, elements, rawFilePath);
        AddToUnitOfWork(document, chunks);

        await _unitOfWork.CompleteAsync(ct);
        return new UploadDocumentResult(document, chunks.Count);
    }

    private IDocumentReader GetReader(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new Exceptions.ValidationException("A file name is required.");
        }

        return _readerFactory.GetReader(fileName);
    }

    private static async Task<byte[]> BufferAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    // Readers may dispose the stream they are given (PdfPig does), so the reader and the
    // file storage each get their own stream over the same buffered bytes.
    private static async Task<List<DocumentElement>> ReadElementsAsync(
        IDocumentReader reader, string fileName, byte[] bytes, CancellationToken ct)
    {
        try
        {
            return await reader.ReadAsync(new MemoryStream(bytes), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not Exceptions.ValidationException)
        {
            throw new Exceptions.ValidationException($"Could not read '{fileName}': {ex.Message}");
        }
    }

    private Task<string> SaveRawFileAsync(string fileName, Guid documentId, byte[] bytes, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName);
        return _fileStorage.SaveAsync(_currentTenant.TenantId, documentId, extension, new MemoryStream(bytes), ct);
    }

    private static Document BuildDocument(
        UploadDocumentCommand request, Guid documentId, List<DocumentElement> elements, string rawFilePath)
    {
        return new Document
        {
            Id = documentId,
            Title = request.Title,
            Content = string.Join("\n\n", elements.Select(e => e.Text)),
            RawFilePath = rawFilePath,
            FilingDate = request.FilingDate
        };
    }

    private void AddToUnitOfWork(Document document, List<DocumentChunk> chunks)
    {
        _unitOfWork.Documents.Add(document);
        foreach (var chunk in chunks)
        {
            chunk.DocumentId = document.Id;
            _unitOfWork.Chunks.Add(chunk);
        }
    }
}
