namespace Lumen.UnitTests.Ask;

public class CitationParserTests
{
    [Fact]
    public void Collects_valid_citations_once()
    {
        var result = CitationParser.Parse("Sales grew [1]. Margin fell [3][1].", sourceCount: 3);

        Assert.Equal(new[] { 1, 3 }, result.Cited.Order());
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Accepts_comma_separated_citations()
    {
        var result = CitationParser.Parse("Both rose [1, 2].", sourceCount: 2);

        Assert.Equal(new[] { 1, 2 }, result.Cited.Order());
    }

    [Fact]
    public void Drops_and_reports_ids_outside_the_provided_sources()
    {
        var result = CitationParser.Parse("Claim [7]. Other [0]. Valid [2].", sourceCount: 5);

        Assert.Equal(new[] { 2 }, result.Cited.Order());
        Assert.Equal(new[] { 0, 7 }, result.Unknown.Order());
    }

    [Fact]
    public void An_answer_without_markers_cites_nothing()
    {
        var result = CitationParser.Parse("No markers here, fiscal 2025.", sourceCount: 3);

        Assert.Empty(result.Cited);
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Ignores_non_ascii_digits_in_brackets_instead_of_throwing()
    {
        var result = CitationParser.Parse("Claim [\u0663] and [\u0661, 2] and [\uFF11]. Valid [2].", sourceCount: 3);

        Assert.Equal(new[] { 2 }, result.Cited.Order());
        Assert.Empty(result.Unknown);
    }
}
