namespace Lumen.Application.Interfaces;

public interface IDocumentReader
{
    Task<List<DocumentElement>> ReadAsync(Stream fileStream, CancellationToken ct = default);
}
