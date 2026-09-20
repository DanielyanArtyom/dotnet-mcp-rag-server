namespace Lumen.Application.Interfaces;

public interface IRepository<TDomain> where TDomain : class
{
    void Add(TDomain entity);
    void Remove(TDomain entity);
    Task<TDomain?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TDomain>> GetAllAsync(CancellationToken ct = default);
}
