namespace Lumen.Infrastructure.Repositories;

public class DocumentRepository : IRepository<Document>
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public DocumentRepository(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public void Add(Document entity)
    {
        var dbEntity = _mapper.Map<DocumentEntity>(entity);
        _context.Documents.Add(dbEntity);
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var dbEntity = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        return dbEntity is null ? null : _mapper.Map<Document>(dbEntity);
    }

    public async Task<List<Document>> GetAllAsync(CancellationToken ct = default)
    {
        var dbEntities = await _context.Documents.ToListAsync(ct);
        return _mapper.Map<List<Document>>(dbEntities);
    }
}
