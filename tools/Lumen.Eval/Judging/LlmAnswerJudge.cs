namespace Lumen.Eval.Judging;

public class LlmAnswerJudge : IAnswerJudge
{
    private readonly IChatClient _client;

    public LlmAnswerJudge(IChatClient client)
    {
        _client = client;
    }

    public async Task<JudgeVerdict> JudgeAsync(string question, string reference, string candidate, CancellationToken ct)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, JudgePromptConstants.SystemPrompt),
            new(ChatRole.User, $"Question: {question}\nReference answer: {reference}\nCandidate answer: {candidate}")
        ];

        var response = await _client.GetResponseAsync(messages, cancellationToken: ct);
        var (correct, reason) = ParseVerdict(response.Text);

        return new JudgeVerdict(correct, reason,
            (int)(response.Usage?.InputTokenCount ?? 0), (int)(response.Usage?.OutputTokenCount ?? 0));
    }

    public static (bool Correct, string Reason) ParseVerdict(string text)
    {
        var line = text.Trim().Split('\n')[0].TrimStart('*', '`', '"', '#', ' ', '\t').TrimEnd();

        foreach (var (prefix, correct) in new[] { ("INCORRECT", false), ("CORRECT", true) })
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (line.Length == prefix.Length || !char.IsLetter(line[prefix.Length])))
            {
                return (correct, line[prefix.Length..].TrimStart(':', '-', ' ', '*', '`', '"').TrimEnd('*', '`', '"', ' '));
            }
        }

        return (false, $"unparseable verdict: {line}");
    }
}
