namespace Lumen.UnitTests.DocumentReading;

public class DocumentReaderFactoryTests
{
    [Theory]
    [InlineData("apple-10k.htm", typeof(HtmlDocumentReader))]
    [InlineData("apple-10k.html", typeof(HtmlDocumentReader))]
    [InlineData("prospectus.pdf", typeof(PdfDocumentReader))]
    [InlineData("REPORT.PDF", typeof(PdfDocumentReader))]
    public void GetReader_picks_the_reader_matching_the_file_extension(string fileName, Type expectedReaderType)
    {
        var factory = new DocumentReaderFactory();

        var reader = factory.GetReader(fileName);

        Assert.IsType(expectedReaderType, reader);
    }

    [Theory]
    [InlineData("notes.docx")]
    [InlineData("README")]
    [InlineData("")]
    public void GetReader_throws_ValidationException_for_an_unsupported_extension(string fileName)
    {
        var factory = new DocumentReaderFactory();

        Assert.Throws<ValidationException>(() => factory.GetReader(fileName));
    }
}
