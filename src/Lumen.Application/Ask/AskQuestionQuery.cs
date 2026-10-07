using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Lumen.Application.Ask;

public record AskQuestionQuery(string Question, Guid? DocumentId, int TopK) : IRequest<AskAnswer>;

public class AskQuestionQueryHandler : IRequestHandler<AskQuestionQuery, AskAnswer>
{
    public const int MaxQuestionLength = 2000;

    private readonly IEmbeddingService _embeddings;
    private readonly IChunkSearchRepository _chunks;
    private readonly IChatService _chat;
    private readonly ILogger<AskQuestionQueryHandler> _logger;

    public AskQuestionQueryHandler(
        IEmbeddingService embeddings, IChunkSearchRepository chunks, IChatService chat, ILogger<AskQuestionQueryHandler> logger)
    {
        _embeddings = embeddings;
        _chunks = chunks;
        _chat = chat;
        _logger = logger;
    }

    public async Task<AskAnswer> Handle(AskQuestionQuery request, CancellationToken ct)
    {
        Validate(request.Question);

        var total = Stopwatch.StartNew();
        var found = await RetrieveAsync(request, ct);
        var retrievalMs = total.ElapsedMilliseconds;

        if (found.Count == 0)
        {
            _logger.LogInformation("No chunks retrieved; refusing without calling the chat model.");
            return Refusal(retrievalMs, total.ElapsedMilliseconds);
        }

        _logger.LogInformation("Retrieved {Count} chunks for /api/ask; top-1 distance {Distance:F4}", found.Count, found[0].Distance);

        var generation = Stopwatch.StartNew();
        var generated = await GenerateAsync(request.Question, found, ct);
        var generationMs = generation.ElapsedMilliseconds;

        var cited = ParseCitations(generated.Text, found.Count);

        return new AskAnswer
        {
            Answer = generated.Text,
            Refused = RagPrompt.IsRefusal(generated.Text),
            Truncated = generated.Truncated,
            Sources = ToSources(found, cited),
            InputTokens = generated.InputTokens,
            OutputTokens = generated.OutputTokens,
            ReasoningTokens = generated.ReasoningTokens,
            RetrievalMs = retrievalMs,
            GenerationMs = generationMs,
            TotalMs = total.ElapsedMilliseconds
        };
    }

    private static void Validate(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new Exceptions.ValidationException("A non-empty question is required.");
        }

        if (question.Length > MaxQuestionLength)
        {
            throw new Exceptions.ValidationException($"A question must be at most {MaxQuestionLength} characters.");
        }
    }

    private async Task<IReadOnlyList<ChunkSearchResult>> RetrieveAsync(AskQuestionQuery request, CancellationToken ct)
    {
        IReadOnlyList<float[]> vectors;
        try
        {
            vectors = await _embeddings.EmbedAsync([request.Question], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalServiceException("Generating an embedding for the question failed.", ex);
        }

        return await _chunks.SearchAsync(vectors[0], request.Question, _embeddings.ModelName, request.TopK, request.DocumentId, ct);
    }

    private async Task<GeneratedAnswer> GenerateAsync(string question, IReadOnlyList<ChunkSearchResult> sources, CancellationToken ct)
    {
        GeneratedAnswer generated;
        try
        {
            generated = await _chat.CompleteAsync(PromptConstants.SystemPrompt, RagPrompt.BuildUserMessage(question, sources), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalServiceException("Generating an answer failed.", ex);
        }

        if (generated.Truncated)
        {
            _logger.LogWarning("The chat model stopped at its output token limit ({OutputTokens} output, {ReasoningTokens} reasoning tokens); the answer may be empty or cut off.",
                generated.OutputTokens, generated.ReasoningTokens);
        }

        return generated;
    }

    private IReadOnlySet<int> ParseCitations(string answer, int sourceCount)
    {
        var citations = CitationParser.Parse(answer, sourceCount);
        if (citations.Unknown.Count > 0)
        {
            _logger.LogWarning("The chat model cited sources that were not provided: {SourceIds}", string.Join(", ", citations.Unknown));
        }

        return citations.Cited;
    }

    private static AskAnswer Refusal(long retrievalMs, long totalMs) => new()
    {
        Answer = PromptConstants.RefusalSentence,
        Refused = true,
        RetrievalMs = retrievalMs,
        TotalMs = totalMs
    };

    private static List<AnswerSource> ToSources(IReadOnlyList<ChunkSearchResult> found, IReadOnlySet<int> cited) =>
        found.Select((chunk, index) => new AnswerSource
        {
            SourceId = index + 1,
            ChunkId = chunk.ChunkId,
            DocumentId = chunk.DocumentId,
            DocumentTitle = chunk.DocumentTitle,
            ChunkIndex = chunk.ChunkIndex,
            ChunkType = chunk.ChunkType,
            TokenCount = chunk.TokenCount,
            Distance = chunk.Distance,
            Score = chunk.Score,
            Cited = cited.Contains(index + 1),
            Text = chunk.Text
        }).ToList();
}
