namespace Lumen.Application.Interfaces;

public interface IDocumentChunkRepository : IRepository<DocumentChunk>
{
    Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
    Task<List<DocumentChunk>> GetByIndexRangeAsync(Guid documentId, int fromIndex, int toIndex, CancellationToken ct = default);
}
