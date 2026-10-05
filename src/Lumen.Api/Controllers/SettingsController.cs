using Lumen.Application.Interfaces;

namespace Lumen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(Role.Admin))]
public class SettingsController : ControllerBase
{
    private const string SearchMode = "vector";

    private readonly ChatSettingsOptions _chat;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ChunkingOptions _chunking;
    private readonly IEmbeddingService _embeddings;

    public SettingsController(
        IOptions<ChatSettingsOptions> chat,
        IOptions<EmbeddingOptions> embeddingOptions,
        IOptions<ChunkingOptions> chunking,
        IEmbeddingService embeddings)
    {
        _chat = chat.Value;
        _embeddingOptions = embeddingOptions.Value;
        _chunking = chunking.Value;
        _embeddings = embeddings;
    }

    [HttpGet]
    public ActionResult<SettingsDto> Get() => Ok(new SettingsDto
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
        SearchMode = SearchMode,
        PromptHash = RagPrompt.Hash
    });
}
