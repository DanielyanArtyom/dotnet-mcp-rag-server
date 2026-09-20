namespace Lumen.UnitTests.Documents;

public class UploadDocumentCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ICurrentTenant> _currentTenant = new();
    private readonly Mock<IDocumentReader> _reader = new();
    private readonly Mock<IDocumentReaderFactory> _readerFactory = new();
    private readonly Mock<IChunker> _chunker = new();
    private readonly Mock<IFileStorage> _fileStorage = new();
    private readonly Mock<IRepository<Document>> _documentRepository = new();
    private readonly Mock<IDocumentChunkRepository> _chunkRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITenantLookupService> _tenantLookup = new();

    public UploadDocumentCommandHandlerTests()
    {
        _currentTenant.Setup(t => t.TenantId).Returns(_tenantId);
        _tenantLookup.Setup(t => t.GetNameAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync("Acme Corp");
        _readerFactory.Setup(f => f.GetReader(It.IsAny<string>())).Returns(_reader.Object);
        _unitOfWork.SetupGet(u => u.Documents).Returns(_documentRepository.Object);
        _unitOfWork.SetupGet(u => u.Chunks).Returns(_chunkRepository.Object);
    }

    private UploadDocumentCommandHandler CreateHandler() =>
        new(_readerFactory.Object, _chunker.Object, _fileStorage.Object, _unitOfWork.Object, _currentTenant.Object, _tenantLookup.Object);

    [Fact]
    public async Task Handle_reads_chunks_stores_the_file_and_persists_the_document()
    {
        var elements = new List<DocumentElement> { new() { Type = ElementType.Paragraph, Text = "Some cleaned text." } };
        _reader.Setup(r => r.ReadAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(elements);

        var chunks = new List<DocumentChunk> { new() { Id = Guid.NewGuid(), ChunkIndex = 0, Text = "Some cleaned text.", TokenCount = 3, ChunkType = ChunkType.Prose } };
        _chunker.Setup(c => c.Chunk(It.IsAny<Guid>(), elements)).Returns(chunks);

        _fileStorage.Setup(s => s.SaveAsync(_tenantId, "Acme Corp", It.IsAny<Guid>(), "Apple 10-K FY2025", ".htm", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{_tenantId}/some-id.htm");

        using var content = new MemoryStream();
        var command = new UploadDocumentCommand("apple-10k.htm", content, "Apple 10-K FY2025", new DateOnly(2025, 10, 31));

        var outcome = await CreateHandler().Handle(command, CancellationToken.None);

        var result = outcome.Document;
        Assert.Equal(1, outcome.ChunkCount);
        _documentRepository.Verify(r => r.Add(It.Is<Document>(d =>
            d.Title == "Apple 10-K FY2025"
            && d.RawFilePath == $"{_tenantId}/some-id.htm"
            && d.Content == "Some cleaned text.")), Times.Once);
        _chunkRepository.Verify(r => r.Add(It.Is<DocumentChunk>(c => c.DocumentId == result.Id)), Times.Once);
        _unitOfWork.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Apple 10-K FY2025", result.Title);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Handle_gives_reader_and_storage_independent_streams_with_the_original_bytes()
    {
        var original = new byte[] { 1, 2, 3, 4, 5 };
        byte[]? readerBytes = null;
        byte[]? storageBytes = null;

        _reader.Setup(r => r.ReadAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>(async (s, _) =>
            {
                using var copy = new MemoryStream();
                await s.CopyToAsync(copy);
                readerBytes = copy.ToArray();
                await s.DisposeAsync();
                return new List<DocumentElement>();
            });
        _chunker.Setup(c => c.Chunk(It.IsAny<Guid>(), It.IsAny<List<DocumentElement>>())).Returns(new List<DocumentChunk>());
        _fileStorage.Setup(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), ".pdf", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, Guid, string, string, Stream, CancellationToken>(async (_, _, _, _, _, s, _) =>
            {
                using var copy = new MemoryStream();
                await s.CopyToAsync(copy);
                storageBytes = copy.ToArray();
                return "path.pdf";
            });

        using var content = new MemoryStream(original);
        var command = new UploadDocumentCommand("filing.pdf", content, "Filing", new DateOnly(2025, 1, 1));

        await CreateHandler().Handle(command, CancellationToken.None);

        Assert.Equal(original, readerBytes);
        Assert.Equal(original, storageBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_throws_ValidationException_when_file_name_is_missing(string? fileName)
    {
        using var content = new MemoryStream();
        var command = new UploadDocumentCommand(fileName!, content, "Title", new DateOnly(2025, 1, 1));

        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().Handle(command, CancellationToken.None));

        _unitOfWork.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_wraps_reader_failures_in_ValidationException_and_saves_nothing()
    {
        _reader.Setup(r => r.ReadAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("corrupt"));

        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        var command = new UploadDocumentCommand("broken.pdf", content, "Title", new DateOnly(2025, 1, 1));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().Handle(command, CancellationToken.None));

        Assert.Contains("broken.pdf", ex.Message);
        _fileStorage.Verify(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
