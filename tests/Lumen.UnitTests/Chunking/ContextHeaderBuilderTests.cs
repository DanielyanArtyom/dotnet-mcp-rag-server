namespace Lumen.UnitTests.Chunking;

public class ContextHeaderBuilderTests
{
    private static readonly ChunkingContext Context = new("Apple 10-k", new DateOnly(2025, 9, 27));

    [Fact]
    public void Includes_title_filing_date_and_section() =>
        Assert.Equal("Apple 10-k (filed 2025-09-27) — Operating Expenses", ContextHeaderBuilder.Build(Context, "Operating Expenses"));

    [Fact]
    public void Omits_the_section_when_none_is_known() =>
        Assert.Equal("Apple 10-k (filed 2025-09-27)", ContextHeaderBuilder.Build(Context, null));
}
