using Lumen.Application.Interfaces;

namespace Lumen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(Role.Admin))]
public class SettingsController : ControllerBase
{
    private readonly ChatSettingsOptions _chat;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ChunkingOptions _chunking;
    private readonly SearchOptions _search;
    private readonly IEmbeddingService _embeddings;
    private readonly IIngestProfileProvider _ingestProfile;

    public SettingsController(
        IOptions<ChatSettingsOptions> chat,
        IOptions<EmbeddingOptions> embeddingOptions,
        IOptions<ChunkingOptions> chunking,
        IOptions<SearchOptions> search,
        IEmbeddingService embeddings,
        IIngestProfileProvider ingestProfile)
    {
        _chat = chat.Value;
        _embeddingOptions = embeddingOptions.Value;
        _chunking = chunking.Value;
        _search = search.Value;
        _embeddings = embeddings;
        _ingestProfile = ingestProfile;
    }

    [HttpGet]
    public ActionResult<SettingsDto> Get()
    {
        var searchMode = _search.Mode.ToString().ToLowerInvariant();
        return Ok(new SettingsDto
        {
            Chat = new ChatSettingsDto
            {
                Provider = _chat.Provider.ToString(),
                Deployment = _chat.Provider == ChatProvider.Fake ? ChatConstants.FakeModelName : _chat.Deployment,
                Temperature = _chat.Temperature,
                TopP = _chat.TopP,
                ReasoningEffort = _chat.ReasoningEffort?.ToString(),
                MaxOutputTokens = _chat.MaxOutputTokens,
                ContextTopK = _chat.ContextTopK
            },
            Embeddings = new EmbeddingSettingsDto
            {
                Provider = _embeddingOptions.Provider.ToString(),
                Model = _embeddings.ModelName
            },
            Chunking = new ChunkingSettingsDto
            {
                MaxTokensPerChunk = _chunking.MaxTokensPerChunk,
                OverlapTokens = _chunking.OverlapTokens
            },
            SearchMode = searchMode,
            Search = new SearchSettingsDto
            {
                Mode = searchMode,
                RrfK = _search.RrfK,
                Candidates = _search.Candidates,
                LexicalWeight = _search.LexicalWeight,
                VectorWeight = _search.VectorWeight,
                MaxChunksPerDocument = _search.MaxChunksPerDocument
            },
            IngestProfile = _ingestProfile.Current,
            PromptHash = RagPrompt.Hash
        });
    }
}
