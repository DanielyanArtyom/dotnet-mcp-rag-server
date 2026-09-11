namespace Lumen.Application.Interfaces;

public interface IUnitOfWork
{
    IRepository<Document> Documents { get; }
    Task<int> CompleteAsync(CancellationToken ct = default);
}
