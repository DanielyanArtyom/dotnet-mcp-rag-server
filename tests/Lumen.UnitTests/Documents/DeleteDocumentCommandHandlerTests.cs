namespace Lumen.UnitTests.Documents;

public class DeleteDocumentCommandHandlerTests
{
    private readonly Mock<IRepository<Document>> _documents = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IFileStorage> _fileStorage = new();

    public DeleteDocumentCommandHandlerTests()
    {
        _unitOfWork.SetupGet(u => u.Documents).Returns(_documents.Object);
    }

    private DeleteDocumentCommandHandler CreateHandler() => new(_unitOfWork.Object, _fileStorage.Object);

    [Fact]
    public async Task Handle_removes_the_document_commits_and_only_then_deletes_the_raw_file()
    {
        var document = new Document { Id = Guid.NewGuid(), RawFilePath = "acme_1/report_2.htm" };
        _documents.Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var calls = new List<string>();
        _unitOfWork.Setup(u => u.CompleteAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("commit")).ReturnsAsync(1);
        _fileStorage.Setup(s => s.DeleteAsync("acme_1/report_2.htm", It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("delete-file")).Returns(Task.CompletedTask);

        await CreateHandler().Handle(new DeleteDocumentCommand(document.Id), CancellationToken.None);

        _documents.Verify(r => r.Remove(document), Times.Once);
        Assert.Equal(["commit", "delete-file"], calls);
    }

    [Fact]
    public async Task Handle_throws_NotFoundException_and_touches_nothing_when_the_document_is_not_visible()
    {
        var id = Guid.NewGuid();
        _documents.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateHandler().Handle(new DeleteDocumentCommand(id), CancellationToken.None));

        _documents.Verify(r => r.Remove(It.IsAny<Document>()), Times.Never);
        _unitOfWork.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Never);
        _fileStorage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
