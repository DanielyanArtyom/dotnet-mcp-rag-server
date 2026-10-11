namespace Lumen.Eval.Agents;

public record AgentRequest(string Question, string Token);

public interface IAgentClient
{
    string ConfiguredModel { get; }
    Task<AgentTranscript> AskAsync(AgentRequest request, CancellationToken ct);
    Task<string> GetVersionAsync(CancellationToken ct);
}
