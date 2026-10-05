namespace Lumen.Eval.Tests.Judging;

public class LlmAnswerJudgeTests
{
    [Theory]
    [InlineData("CORRECT: same figure and period", true, "same figure and period")]
    [InlineData("correct - matches", true, "matches")]
    [InlineData("INCORRECT: wrong fiscal year", false, "wrong fiscal year")]
    [InlineData("  INCORRECT: omits the amount\nextra line", false, "omits the amount")]
    [InlineData("**CORRECT**: same figure", true, "same figure")]
    [InlineData("**INCORRECT: wrong year**", false, "wrong year")]
    [InlineData("## `CORRECT` - matches", true, "matches")]
    [InlineData("\"CORRECT\"", true, "")]
    public void Parses_the_verdict_line(string text, bool correct, string reason)
    {
        var verdict = LlmAnswerJudge.ParseVerdict(text);

        Assert.Equal(correct, verdict.Correct);
        Assert.Equal(reason, verdict.Reason);
    }

    [Theory]
    [InlineData("CORRECTLY answered, I think")]
    [InlineData("Incorrectness noted")]
    public void A_prefix_that_continues_into_a_longer_word_is_unparseable(string text)
    {
        var verdict = LlmAnswerJudge.ParseVerdict(text);

        Assert.False(verdict.Correct);
        Assert.StartsWith("unparseable verdict", verdict.Reason);
    }

    [Fact]
    public void An_unparseable_verdict_counts_as_incorrect()
    {
        var verdict = LlmAnswerJudge.ParseVerdict("I think it is mostly right");

        Assert.False(verdict.Correct);
        Assert.StartsWith("unparseable verdict", verdict.Reason);
    }
}
