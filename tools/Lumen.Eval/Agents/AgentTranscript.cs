namespace Lumen.Eval.Agents;

public class AgentTranscript
{
    public string Answer { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public bool TimedOut { get; set; }
    public string? Model { get; set; }
    public int Turns { get; set; }
    public List<string> ToolCalls { get; set; } = [];
    public List<string> ToolResults { get; set; } = [];
    public List<string> PermissionDenials { get; set; } = [];
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public long DurationMs { get; set; }
}
