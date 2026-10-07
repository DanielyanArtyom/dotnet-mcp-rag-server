namespace Lumen.Eval.Constants;

public static class JudgePromptConstants
{
    public const string SystemPrompt = """
        You grade answers to questions about company filings.
        Compare the candidate answer with the reference answer.
        The candidate is CORRECT if it states the same key facts as the reference; wording may differ and extra correct detail is fine.
        It is INCORRECT if it contradicts the reference, omits its key fact, gives a different period or amount, or refuses to answer.
        Ignore citation markers such as [1].
        Reply with exactly one line: "CORRECT: <short reason>" or "INCORRECT: <short reason>".
        """;
}
