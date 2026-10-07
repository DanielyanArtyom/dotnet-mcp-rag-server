namespace Lumen.Domain.Models;

public class GeneratedAnswer
{
    public string Text { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public bool Truncated { get; set; }
}
