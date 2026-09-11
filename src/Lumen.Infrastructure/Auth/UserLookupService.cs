namespace Lumen.Infrastructure.Auth;

public class UserLookupService : IUserLookupService
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public UserLookupService(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    // The ONLY place in the codebase allowed to bypass the tenant query filter:
    // at login time we don't know the caller's tenant yet — we're finding it out
    // from a globally-unique email. Every other query must go through the filter.
    public async Task<User?> FindByEmailAcrossTenantsAsync(string email, CancellationToken ct = default)
    {
        var entity = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        return entity is null ? null : _mapper.Map<User>(entity);
    }
}
