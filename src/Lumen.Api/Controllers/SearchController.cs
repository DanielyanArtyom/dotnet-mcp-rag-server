using Lumen.Application.Search;

namespace Lumen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public SearchController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    [HttpPost]
    public async Task<ActionResult<List<SearchResultDto>>> Search([FromBody] SearchRequest request, CancellationToken ct)
    {
        var results = await _mediator.Send(new SearchChunksQuery(request.Query, request.TopK, request.DocumentId), ct);

        return Ok(_mapper.Map<List<SearchResultDto>>(results));
    }
}
