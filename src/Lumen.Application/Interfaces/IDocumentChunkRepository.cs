namespace Lumen.Application.Interfaces;

public interface IDocumentChunkRepository : IRepository<DocumentChunk>
{
    Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
}
