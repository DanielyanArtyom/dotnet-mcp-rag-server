using System.ComponentModel.DataAnnotations;

namespace Lumen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private const int MaxUploadBytes = 20_971_520;

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

    [HttpPost("upload")]
    [Authorize(Roles = nameof(Role.Admin))]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<DocumentDto>> Upload(IFormFile? file, [FromForm, Required, MaxLength(500)] string title, [FromForm, Required] DateOnly? filingDate, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("A non-empty file is required.");

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadDocumentCommand(file.FileName, stream, title, filingDate!.Value), ct);

        var dto = _mapper.Map<DocumentDto>(result.Document);
        dto.ChunkCount = result.ChunkCount;
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpGet("{id:guid}/chunks")]
    public async Task<ActionResult<List<DocumentChunkDto>>> GetChunks(Guid id, CancellationToken ct)
    {
        var document = await _mediator.Send(new GetDocumentByIdQuery(id), ct);
        if (document is null)
            return NotFound();

        var chunks = await _mediator.Send(new GetDocumentChunksQuery(id), ct);
        return Ok(_mapper.Map<List<DocumentChunkDto>>(chunks));
    }
}
