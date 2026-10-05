namespace Lumen.Eval.Tests.Scoring;

public class AnchorMatcherTests
{
    [Fact]
    public void Normalizes_case_whitespace_typographic_quotes_dashes_and_table_pipes()
    {
        Assert.Equal("the company's net sales - total $ 9,220",
            TextNormalizer.Normalize("The  Company’s\nnet sales – Total | $ | 9,220"));
    }

    [Fact]
    public void A_chunk_is_relevant_when_it_contains_every_string_of_one_group()
    {
        List<List<string>> anchors = [["Fully taxable-equivalent adjustments", "9,220"], ["never present"]];

        Assert.True(AnchorMatcher.IsRelevant("Other income | $ | 9,220 | $ | 1,151 … Fully taxable‑equivalent adjustments(a)", anchors));
    }

    [Fact]
    public void A_chunk_missing_one_string_of_every_group_is_not_relevant()
    {
        List<List<string>> anchors = [["CET1 ratio", "15.7%"]];

        Assert.False(AnchorMatcher.IsRelevant("Our CET1 ratio was strong.", anchors));
    }

    [Fact]
    public void No_anchors_means_nothing_is_relevant() =>
        Assert.False(AnchorMatcher.IsRelevant("anything", []));
}
