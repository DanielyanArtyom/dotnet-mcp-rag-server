namespace Lumen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("token")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var token = await _mediator.Send(new LoginCommand(request.Email, request.Password), ct);
        return Ok(new TokenResponse(token));
    }
}
