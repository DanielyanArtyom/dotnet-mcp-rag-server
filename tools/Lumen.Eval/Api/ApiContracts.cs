namespace Lumen.Eval.Api;

public record LoginResponse(string Token);

public record DocumentInfo(Guid Id, string Title, string? IngestProfile = null);

public record DocumentChunkInfo(int ChunkIndex, string Text, int TokenCount, string ChunkType);

public record SearchHit(Guid ChunkId, Guid DocumentId, string DocumentTitle, int ChunkIndex, string ChunkType, int TokenCount, string Text, double Distance, double? Score = null);

public record AskSource(int SourceId, Guid ChunkId, Guid DocumentId, string DocumentTitle, int ChunkIndex, string ChunkType, int TokenCount, double Distance, bool Cited, string Text, double? Score = null);

public record AskUsage(int InputTokens, int OutputTokens, int ReasoningTokens);

public record AskTimings(long RetrievalMs, long GenerationMs, long TotalMs);

public record AskResult(string Answer, bool Refused, bool Truncated, List<AskSource> Sources, AskUsage Usage, AskTimings Timings);
