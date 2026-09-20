using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Lumen.UnitTests.DocumentReading;

public class PdfDocumentReaderTests
{
    [Fact]
    public async Task ReadAsync_separates_prose_from_the_table_grid()
    {
        var reader = new PdfDocumentReader();
        using var stream = new MemoryStream(BuildPdf(
            Prose("Net sales increased due to strength in Services."),
            Row("Segment", "Net sales", "Change"),
            Row("Americas", "45781", "7%"),
            Row("Europe", "27511", "4%")));

        var elements = await reader.ReadAsync(stream);

        var paragraph = Assert.Single(elements, e => e.Type == ElementType.Paragraph);
        Assert.Equal("Net sales increased due to strength in Services.", paragraph.Text);

        var table = Assert.Single(elements, e => e.Type == ElementType.Table);
        Assert.Equal(
            "Segment | Net sales | Change\nAmericas | 45781 | 7%\nEurope | 27511 | 4%",
            table.Text);
    }

    [Fact]
    public async Task ReadAsync_preserves_document_order_around_a_table()
    {
        var elements = await ReadAsync(BuildPdf(
            Prose("Before."), Row("A", "B", "C"), Row("D", "E", "F"), Prose("After.")));

        Assert.Equal(
            [ElementType.Paragraph, ElementType.Table, ElementType.Paragraph],
            elements.Select(e => e.Type));
        Assert.Equal("A | B | C\nD | E | F", elements[1].Text);
    }

    [Fact]
    public async Task ReadAsync_splits_tables_separated_by_prose()
    {
        var elements = await ReadAsync(BuildPdf(
            Row("A", "B", "C"), Prose("Between."), Row("D", "E", "F")));

        Assert.Equal(
            [ElementType.Table, ElementType.Paragraph, ElementType.Table],
            elements.Select(e => e.Type));
    }

    [Fact]
    public async Task ReadAsync_returns_nothing_for_a_page_without_text()
    {
        var elements = await ReadAsync(BuildPdf());

        Assert.Empty(elements);
    }

    private static Task<List<DocumentElement>> ReadAsync(byte[] pdf) =>
        new PdfDocumentReader().ReadAsync(new MemoryStream(pdf));

    private static string[] Prose(string text) => [text];

    private static string[] Row(params string[] cells) => cells;

    private static byte[] BuildPdf(params string[][] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);
        double[] columnX = [50, 200, 400];

        for (var r = 0; r < lines.Length; r++)
            for (var c = 0; c < lines[r].Length; c++)
                page.AddText(lines[r][c], 12, new PdfPoint(lines[r].Length == 1 ? 50 : columnX[c], 780 - r * 20), font);

        return builder.Build();
    }
}
