namespace Lumen.Eval.Tests.Scoring;

public class NumberScorerTests
{
    [Theory]
    [InlineData("Total net sales were $416,161 million [1].")]
    [InlineData("Total net sales were $416.2 billion [1].")]
    [InlineData("Net sales: 416,161 (in millions) [2]")]
    public void Matches_a_value_in_millions_written_several_ways(string answer) =>
        Assert.True(NumberScorer.Matches(answer, 416161m, "millions", null));

    [Theory]
    [InlineData("Other comprehensive loss was (1,234) [3].")]
    [InlineData("Other comprehensive loss was -1,234 [3].")]
    [InlineData("Other comprehensive loss was −1,234 [3].")]
    [InlineData("Other comprehensive loss was $(1,234) million [3].")]
    public void Reads_parentheses_and_minus_signs_as_negative(string answer) =>
        Assert.True(NumberScorer.Matches(answer, -1234m, "millions", null));

    [Fact]
    public void A_positive_number_does_not_match_a_negative_expectation() =>
        Assert.False(NumberScorer.Matches("The loss was 1,234 [1].", -1234m, "millions", null));

    [Fact]
    public void Citation_markers_are_never_read_as_numbers() =>
        Assert.False(NumberScorer.Matches("See the filing [3].", 3m, null, null));

    [Fact]
    public void Non_ascii_digits_are_ignored_instead_of_throwing()
    {
        Assert.Empty(NumberScorer.ExtractCandidates("Sales were \u0661\u0662\u0663 [\u0661] and \uFF11\uFF12.", null));
        Assert.False(NumberScorer.Matches("\u0661\u0662\u0663", 123m, null, null));
    }

    [Fact]
    public void Matches_percentages() =>
        Assert.True(NumberScorer.Matches("The CET1 ratio was 15.7% [1].", 15.7m, "percent", null));

    [Fact]
    public void Respects_the_relative_tolerance()
    {
        Assert.False(NumberScorer.Matches("Net income was $37.0 billion.", 37649m, "millions", null));
        Assert.True(NumberScorer.Matches("Net income was $37.0 billion.", 37649m, "millions", 0.02m));
    }

    [Fact]
    public void A_refusal_has_no_matching_number() =>
        Assert.False(NumberScorer.Matches("The provided documents do not contain the answer to this question.", 416161m, "millions", null));

    [Fact]
    public void Rejects_an_unknown_unit() =>
        Assert.Throws<ArgumentException>(() => NumberScorer.Matches("1", 1m, "furlongs", null));

    [Theory]
    [InlineData("- $416,161 million [1]")]
    [InlineData("Net sales - 416,161 [1]")]
    public void A_spaced_hyphen_is_a_separator_not_a_minus(string answer)
    {
        Assert.True(NumberScorer.Matches(answer, 416161m, "millions", null));
        Assert.DoesNotContain(NumberScorer.ExtractCandidates(answer, "millions"), c => c < 0);
    }

    [Fact]
    public void A_hyphen_inside_a_range_is_not_a_minus() =>
        Assert.DoesNotContain(NumberScorer.ExtractCandidates("fiscal 2024-2025 and 5-6", null), c => c < 0);

    [Fact]
    public void Reads_a_minus_before_a_dollar_sign_as_negative() =>
        Assert.True(NumberScorer.Matches("The loss was -$1,234 million.", -1234m, "millions", null));
}
