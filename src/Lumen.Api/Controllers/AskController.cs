namespace Lumen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AskController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly ChatSettingsOptions _chatOptions;

    public AskController(IMediator mediator, IMapper mapper, IOptions<ChatSettingsOptions> chatOptions)
    {
        _mediator = mediator;
        _mapper = mapper;
        _chatOptions = chatOptions.Value;
    }

    [HttpPost]
    public async Task<ActionResult<AskResponseDto>> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        var answer = await _mediator.Send(new AskQuestionQuery(request.Question, request.DocumentId, _chatOptions.ContextTopK), ct);

        return Ok(_mapper.Map<AskResponseDto>(answer));
    }
}
