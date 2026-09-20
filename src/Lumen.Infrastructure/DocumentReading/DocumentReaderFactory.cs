namespace Lumen.Infrastructure.DocumentReading;

public class DocumentReaderFactory : IDocumentReaderFactory
{
    private static readonly FileExtensionContentTypeProvider MimeTypeProvider = new();

    public IDocumentReader GetReader(string fileName)
    {
        if (!MimeTypeProvider.TryGetContentType(fileName, out var mimeType))
            throw new ValidationException($"Unsupported file type: '{fileName}'.");

        return mimeType switch
        {
            "text/html" => new HtmlDocumentReader(),
            "application/pdf" => new PdfDocumentReader(),
            _ => throw new ValidationException($"Unsupported file type: '{fileName}' ({mimeType}).")
        };
    }
}
