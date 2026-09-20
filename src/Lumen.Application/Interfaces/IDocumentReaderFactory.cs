namespace Lumen.Application.Interfaces;

public interface IDocumentReaderFactory
{
    IDocumentReader GetReader(string fileName);
}
