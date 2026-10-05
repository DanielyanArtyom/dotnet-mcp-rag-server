namespace Lumen.Api.DTO.Responses;

public class AskResponseDto
{
    public string Answer { get; set; } = default!;
    public bool Refused { get; set; }
    public bool Truncated { get; set; }
    public List<AskSourceDto> Sources { get; set; } = [];
    public AskUsageDto Usage { get; set; } = new();
    public AskTimingsDto Timings { get; set; } = new();
}

public class AskSourceDto
{
    public int SourceId { get; set; }
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = default!;
    public int ChunkIndex { get; set; }
    public string ChunkType { get; set; } = default!;
    public int TokenCount { get; set; }
    public double Distance { get; set; }
    public bool Cited { get; set; }
    public string Text { get; set; } = default!;
}

public class AskUsageDto
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
}

public class AskTimingsDto
{
    public long RetrievalMs { get; set; }
    public long GenerationMs { get; set; }
    public long TotalMs { get; set; }
}
