namespace Lumen.UnitTests.Chunking;

public class SectionHeadingTests
{
    [Theory]
    [InlineData("Gross Margin")]
    [InlineData("Capital Return Program")]
    [InlineData("Item 7. Management’s Discussion and Analysis")]
    [InlineData("Mac")]
    [InlineData("Services (1)")]
    public void Recognises_heading_like_paragraphs(string text) => Assert.True(SectionHeading.IsHeadingLike(text));

    [Theory]
    [InlineData("Net sales increased during 2025.")]
    [InlineData("First Quarter 2025:")]
    [InlineData("•MacBook Pro")]
    [InlineData("(1) Services net sales include amortization")]
    [InlineData("Americas | 45781")]
    [InlineData("2025")]
    [InlineData("This sentence has far too many words to be any kind of section heading")]
    public void Rejects_sentences_bullets_tables_and_bare_numbers(string text) => Assert.False(SectionHeading.IsHeadingLike(text));
}
