namespace Lumen.Application.Interfaces;

public interface IFileStorage
{
    Task<string> SaveAsync(Guid tenantId, string tenantName, Guid documentId, string documentName, string extension, Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}
