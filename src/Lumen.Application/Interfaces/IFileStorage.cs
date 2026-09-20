namespace Lumen.Application.Interfaces;

public interface IFileStorage
{
    Task<string> SaveAsync(Guid tenantId, Guid documentId, string extension, Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);
}
