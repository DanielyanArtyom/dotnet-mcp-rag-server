namespace Lumen.UnitTests.Documents;

public class CreateDocumentCommandHandlerTests
{
    [Fact]
    public async Task Handle_adds_the_document_and_completes_the_unit_of_work()
    {
        var repository = new Mock<IRepository<Document>>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Documents).Returns(repository.Object);

        var handler = new CreateDocumentCommandHandler(unitOfWork.Object);
        var command = new CreateDocumentCommand("Title", "Content");

        var result = await handler.Handle(command, CancellationToken.None);

        repository.Verify(r => r.Add(It.Is<Document>(d => d.Title == "Title" && d.Content == "Content")), Times.Once);
        unitOfWork.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Title", result.Title);
    }
}
