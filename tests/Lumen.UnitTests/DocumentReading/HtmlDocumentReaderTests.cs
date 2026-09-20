namespace Lumen.UnitTests.DocumentReading;

public class HtmlDocumentReaderTests
{
    [Fact]
    public async Task ReadAsync_classifies_heading_paragraph_and_table_separately()
    {
        const string html = """
            <html><body>
              <h1>Item 7. MD&amp;A</h1>
              <p>Net sales increased due to strength in Services.</p>
              <div><span>Extra prose wrapped in a div, not a p tag.</span></div>
              <table>
                <tr><td>Segment</td><td>Net Sales</td></tr>
                <tr><td>Americas</td><td>45781</td></tr>
              </table>
            </body></html>
            """;

        var reader = new HtmlDocumentReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var elements = await reader.ReadAsync(stream);

        Assert.Equal(ElementType.Heading, elements[0].Type);
        Assert.Contains("MD&A", elements[0].Text);
        Assert.Equal(ElementType.Paragraph, elements[1].Type);
        Assert.Equal(ElementType.Paragraph, elements[2].Type);
        Assert.Contains("Extra prose", elements[2].Text);
        Assert.Equal(ElementType.Table, elements[3].Type);
        Assert.Contains("Americas", elements[3].Text);
        Assert.Contains("45781", elements[3].Text);
    }

    [Fact]
    public async Task ReadAsync_ignores_empty_spacer_divs()
    {
        const string html = "<html><body><div style=\"margin-top:9pt\"><span><br/></span></div><p>Real content.</p></body></html>";
        var reader = new HtmlDocumentReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var elements = await reader.ReadAsync(stream);

        Assert.Single(elements);
        Assert.Equal("Real content.", elements[0].Text);
    }

    [Fact]
    public async Task ReadAsync_does_not_flatten_a_div_that_contains_a_nested_table()
    {
        const string html = "<html><body><div><table><tr><td>A</td><td>B</td></tr></table></div></body></html>";
        var reader = new HtmlDocumentReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var elements = await reader.ReadAsync(stream);

        Assert.Single(elements);
        Assert.Equal(ElementType.Table, elements[0].Type);
    }

    [Fact]
    public async Task ReadAsync_collapses_non_breaking_spaces_and_entities()
    {
        const string html = "<html><body><div><span>Total&#160;&nbsp;net   sales rose.</span></div></body></html>";
        var reader = new HtmlDocumentReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var elements = await reader.ReadAsync(stream);

        Assert.Single(elements);
        Assert.Equal("Total net sales rose.", elements[0].Text);
    }

    private static async Task<List<DocumentElement>> Read(string html)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return await new HtmlDocumentReader().ReadAsync(stream);
    }

    [Fact]
    public async Task ReadAsync_keeps_text_next_to_a_nested_table_in_document_order()
    {
        var elements = await Read("<html><body>Loose text<div>Intro <table><tr><td>A</td></tr></table></div><div><span>Note</span><table><tr><td>B</td></tr></table></div></body></html>");

        Assert.Equal(["Loose text", "Intro", "A", "Note", "B"], elements.Select(e => e.Text));
        Assert.Equal(ElementType.Table, elements[2].Type);
    }

    [Fact]
    public async Task ReadAsync_emits_text_of_list_pre_blockquote_font_and_section()
    {
        var elements = await Read("<html><body><ul><li>one</li><li>two</li></ul><pre>code  here</pre><blockquote>quoted</blockquote><section><font>in font</font></section><center>mid</center></body></html>");

        Assert.Equal(["one", "two", "code here", "quoted", "in font", "mid"], elements.Select(e => e.Text));
        Assert.All(elements, e => Assert.Equal(ElementType.Paragraph, e.Type));
    }

    [Fact]
    public async Task ReadAsync_does_not_duplicate_rows_of_nested_tables()
    {
        var elements = await Read("<html><body><table><tr><td>Outer<table><tr><td>Inner</td></tr></table></td><td>X</td></tr></table></body></html>");

        var table = Assert.Single(elements);
        Assert.DoesNotContain("\n", table.Text);
        Assert.Equal(1, table.Text.Split("Inner").Length - 1);
    }

    [Fact]
    public async Task ReadAsync_descends_into_div_with_block_children_instead_of_flattening()
    {
        var elements = await Read("<html><body><div><h2>Title</h2><p>First.</p><div>Second.</div></div></body></html>");

        Assert.Equal([ElementType.Heading, ElementType.Paragraph, ElementType.Paragraph], elements.Select(e => e.Type));
        Assert.Equal(["Title", "First.", "Second."], elements.Select(e => e.Text));
    }

    [Fact]
    public async Task ReadAsync_treats_br_as_a_word_separator()
    {
        var elements = await Read("<html><body><p>a<br>b</p></body></html>");

        Assert.Equal("a b", Assert.Single(elements).Text);
    }

    [Fact]
    public async Task ReadAsync_skips_display_none_and_ix_header_metadata()
    {
        var elements = await Read("<html><body><div style=\"display:none\"><ix:header><ix:hidden>context</ix:hidden></ix:header></div><ix:header>meta</ix:header><div style=\"display: none;\">x</div><p>Real</p></body></html>");

        Assert.Equal("Real", Assert.Single(elements).Text);
    }
}
