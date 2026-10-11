namespace Lumen.Eval.Constants;

public static class AgentPromptConstants
{
    public const string RefusalSentence = "The provided documents do not contain the answer to this question.";

    public const string Rules =
        "Answer the question using only what the Lumen MCP tools return. Do not use prior knowledge. " +
        "Cite the passages you used by their [n] number. Be concise: give the figure or fact asked for. Always answer in English. " +
        "If the tools do not return the answer, reply with exactly this sentence and nothing else: " + RefusalSentence;

    public static readonly string RulesHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Rules)))[..12].ToLowerInvariant();
}
