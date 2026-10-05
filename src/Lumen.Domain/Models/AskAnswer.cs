namespace Lumen.Domain.Models;

public class AskAnswer
{
    public string Answer { get; set; } = string.Empty;
    public bool Refused { get; set; }
    public bool Truncated { get; set; }
    public List<AnswerSource> Sources { get; set; } = [];
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public long RetrievalMs { get; set; }
    public long GenerationMs { get; set; }
    public long TotalMs { get; set; }
}
