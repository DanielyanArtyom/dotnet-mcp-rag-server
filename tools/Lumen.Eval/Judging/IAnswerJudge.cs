namespace Lumen.Eval.Judging;

public record JudgeVerdict(bool Correct, string Reason, int InputTokens, int OutputTokens);

public interface IAnswerJudge
{
    Task<JudgeVerdict> JudgeAsync(string question, string reference, string candidate, CancellationToken ct);
}
