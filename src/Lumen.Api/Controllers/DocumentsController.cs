namespace Lumen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public DocumentsController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> GetAll(CancellationToken ct)
    {
        var documents = await _mediator.Send(new GetDocumentsQuery(), ct);
        return Ok(_mapper.Map<List<DocumentDto>>(documents));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetById(Guid id, CancellationToken ct)
    {
        var document = await _mediator.Send(new GetDocumentByIdQuery(id), ct);
        return document is null ? NotFound() : Ok(_mapper.Map<DocumentDto>(document));
    }

    [HttpPost]
    public async Task<ActionResult<DocumentDto>> Create(CreateDocumentCommand command, CancellationToken ct)
    {
        var document = await _mediator.Send(command, ct);
        var dto = _mapper.Map<DocumentDto>(document);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }
}
