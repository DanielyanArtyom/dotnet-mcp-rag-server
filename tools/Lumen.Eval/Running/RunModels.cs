namespace Lumen.Eval.Running;

public enum RunMode
{
    Retrieval,
    Full,
    Agent
}

public static class RunModeExtensions
{
    public static bool IsAnswering(this RunMode mode) => mode is RunMode.Full or RunMode.Agent;
}

public class RunOptions
{
    public RunMode Mode { get; set; }
    public string Label { get; set; } = default!;
    public int Repeat { get; set; } = 1;
    public bool Holdout { get; set; }
}

public class TenantCredentials
{
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
}

public record CorpusDocument(string Tenant, string Title, string? IngestProfile, int ChunkCount);

public record SourceRecord(int Rank, Guid ChunkId, Guid DocumentId, int ChunkIndex, string ChunkType, double Distance, double? Score, string TextPreview);

public class QuestionResult
{
    public string Id { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Tenant { get; set; } = default!;
    public ExpectedKind ExpectedKind { get; set; }
    public int Repeat { get; set; }
    public bool Labelable { get; set; }
    public int? FirstHitRank { get; set; }
    public int RetrievedTokens { get; set; }
    public bool? Correct { get; set; }
    public bool? Refused { get; set; }
    public bool? Truncated { get; set; }
    public string? JudgeReason { get; set; }
    public string? Answer { get; set; }
    public List<int> CitedSourceIds { get; set; } = [];
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public long LatencyMs { get; set; }
    public long? ServerTotalMs { get; set; }
    public List<SourceRecord> Sources { get; set; } = [];
    public bool? RetrievedRelevant { get; set; }
    public List<string> ToolCalls { get; set; } = [];
    public List<Guid> SeenChunkIds { get; set; } = [];
    public int? Turns { get; set; }
    public decimal? AgentCostUsd { get; set; }
}

public class RunHeader
{
    public string Label { get; set; } = default!;
    public RunMode Mode { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string GitCommit { get; set; } = "unknown";
    public string EvalSetPath { get; set; } = default!;
    public string EvalSetSha256 { get; set; } = default!;
    public int Repeat { get; set; }
    public bool Holdout { get; set; }
    public JsonElement ServerSettings { get; set; }
    public string? IngestProfile { get; set; }
    public List<CorpusDocument> Corpus { get; set; } = [];
    public List<string> Unlabelable { get; set; } = [];
    public Dictionary<string, int> BroadAnchors { get; set; } = [];
    public string? JudgeDeployment { get; set; }
    public int JudgeInputTokens { get; set; }
    public int JudgeOutputTokens { get; set; }
    public string? AbortReason { get; set; }
    public string? AgentModel { get; set; }
    public string? AgentConfiguredModel { get; set; }
    public string? AgentCliVersion { get; set; }
    public string? AgentRulesHash { get; set; }

    public int? ContextTopK() =>
        ServerSettings.ValueKind == JsonValueKind.Object
        && ServerSettings.TryGetProperty("chat", out var chat) && chat.ValueKind == JsonValueKind.Object
        && chat.TryGetProperty("contextTopK", out var topK) && topK.TryGetInt32(out var value)
            ? value
            : null;
}

public class RunOutcome
{
    public RunHeader Header { get; set; } = default!;
    public List<QuestionResult> Results { get; set; } = [];
}

public class IsolationBreachException(string message) : Exception(message);

public class EvalRunAbortedException(string message, Exception inner, RunOutcome partial) : Exception(message, inner)
{
    public RunOutcome Partial { get; } = partial;
}
