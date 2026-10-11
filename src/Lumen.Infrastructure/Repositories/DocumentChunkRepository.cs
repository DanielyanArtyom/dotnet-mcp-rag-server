namespace Lumen.Infrastructure.Repositories;

public class DocumentChunkRepository : IDocumentChunkRepository
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public DocumentChunkRepository(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public void Add(DocumentChunk entity)
    {
        var dbEntity = _mapper.Map<DocumentChunkEntity>(entity);
        _context.DocumentChunks.Add(dbEntity);
    }

    public void Remove(DocumentChunk entity)
    {
        var dbEntity = _context.DocumentChunks.Local.FirstOrDefault(c => c.Id == entity.Id)
                       ?? _mapper.Map<DocumentChunkEntity>(entity);
        _context.DocumentChunks.Remove(dbEntity);
    }

    public async Task<DocumentChunk?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var dbEntity = await _context.DocumentChunks.FirstOrDefaultAsync(c => c.Id == id, ct);
        return dbEntity is null ? null : _mapper.Map<DocumentChunk>(dbEntity);
    }

    public async Task<List<DocumentChunk>> GetAllAsync(CancellationToken ct = default)
    {
        var dbEntities = await _context.DocumentChunks.ToListAsync(ct);
        return _mapper.Map<List<DocumentChunk>>(dbEntities);
    }

    public async Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default)
    {
        var dbEntities = await _context.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
        return _mapper.Map<List<DocumentChunk>>(dbEntities);
    }

    public async Task<List<DocumentChunk>> GetByIndexRangeAsync(Guid documentId, int fromIndex, int toIndex, CancellationToken ct = default)
    {
        var dbEntities = await _context.DocumentChunks
            .Where(c => c.DocumentId == documentId && c.ChunkIndex >= fromIndex && c.ChunkIndex <= toIndex)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
        return _mapper.Map<List<DocumentChunk>>(dbEntities);
    }
}
