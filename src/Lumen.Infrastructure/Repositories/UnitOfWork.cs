namespace Lumen.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context, IRepository<Document> documents)
    {
        _context = context;
        Documents = documents;
    }

    public IRepository<Document> Documents { get; }

    public Task<int> CompleteAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
