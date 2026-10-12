namespace Lumen.UnitTests.Chunking;

public class TableCaptionTests
{
    private static DocumentElement Paragraph(string text) => new() { Type = ElementType.Paragraph, Text = text };

    [Fact]
    public void A_short_paragraph_ending_with_a_colon_is_the_caption() =>
        Assert.Equal("Operating expenses for 2025, 2024 and 2023 were as follows (dollars in millions):",
            TableCaption.Find(Paragraph("Operating expenses for 2025, 2024 and 2023 were as follows (dollars in millions):")));

    [Fact]
    public void The_matching_sentence_of_a_long_paragraph_is_cut_to_forty_words()
    {
        const string dimon = "The following table provides information concerning Rule 10b5-1 trading arrangements (as defined in Item 408 of Regulation S-K under the Securities Exchange Act of 1934) adopted in the fourth quarter of 2025 by any director or officer who is subject to the filing requirements of Section 16 of the Securities Exchange Act of 1934 (each a “Section 16 Director or Officer”). These trading arrangements are intended to satisfy the affirmative defense of Rule 10b5-1(c). No non-Rule 10b5-1 trading arrangements were adopted.";

        var caption = TableCaption.Find(Paragraph(dimon))!;

        Assert.StartsWith("The following table provides information concerning Rule 10b5-1 trading arrangements", caption);
        Assert.EndsWith(" …", caption);
        Assert.Equal(TableCaption.MaxWords + 1, caption.Split(' ').Length);
    }

    [Fact]
    public void A_paragraph_without_a_marker_or_a_colon_is_not_a_caption() =>
        Assert.Null(TableCaption.Find(Paragraph("Net sales increased primarily due to higher iPhone revenue.")));

    [Fact]
    public void A_heading_or_a_table_before_the_table_gives_no_caption()
    {
        Assert.Null(TableCaption.Find(new DocumentElement { Type = ElementType.Heading, Text = "Summary of results:" }));
        Assert.Null(TableCaption.Find(new DocumentElement { Type = ElementType.Table, Text = "A | 1" }));
        Assert.Null(TableCaption.Find(null));
    }

    [Fact]
    public void A_huge_unpunctuated_paragraph_is_still_cut_to_forty_words()
    {
        var text = "The following table shows " + string.Join(" ", Enumerable.Repeat("item", 500));

        Assert.Equal(TableCaption.MaxWords + 1, TableCaption.Find(Paragraph(text))!.Split(' ').Length);
    }

    [Fact]
    public void Abbreviations_and_initials_do_not_end_a_sentence() =>
        Assert.Equal("The following table presents the Firm's U.S. GAAP results:",
            TableCaption.Find(Paragraph("The following table presents the Firm's U.S. GAAP results:")));

    [Fact]
    public void Company_abbreviations_do_not_end_a_sentence() =>
        Assert.Equal("Amounts owed to Acme Co. LLC, Initech Corp. Holdings and Apple Inc. Europe under Plan No. A-4 were as follows:",
            TableCaption.Find(Paragraph("Revenue grew. Amounts owed to Acme Co. LLC, Initech Corp. Holdings and Apple Inc. Europe under Plan No. A-4 were as follows:")));
}
