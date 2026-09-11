namespace Lumen.Application.Auth;

public record LoginCommand(string Email, string Password) : IRequest<string>;

public class LoginCommandHandler : IRequestHandler<LoginCommand, string>
{
    private readonly IUserLookupService _userLookup;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public LoginCommandHandler(
        IUserLookupService userLookup,
        IPasswordHasher<User> passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _userLookup = userLookup;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<string> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _userLookup.FindByEmailAcrossTenantsAsync(request.Email, ct)
            ?? throw new UnauthorizedException("Invalid email or password.");

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        return _jwtTokenService.GenerateToken(user.Id, user.TenantId, user.Role);
    }
}
