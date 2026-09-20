namespace Lumen.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context, IRepository<Document> documents, IDocumentChunkRepository chunks)
    {
        _context = context;
        Documents = documents;
        Chunks = chunks;
    }

    public IRepository<Document> Documents { get; }
    public IDocumentChunkRepository Chunks { get; }

    public Task<int> CompleteAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
