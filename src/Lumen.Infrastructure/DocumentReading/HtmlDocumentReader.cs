using AngleSharp;
using AngleSharp.Dom;

namespace Lumen.Infrastructure.DocumentReading;

public class HtmlDocumentReader : IDocumentReader
{
    public const int MaxSpan = 100;

    public async Task<List<DocumentElement>> ReadAsync(Stream fileStream, CancellationToken ct = default)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(fileStream), ct);

        var elements = new List<DocumentElement>();
        if (document.Body is not null)
            WalkNode(document.Body, elements);

        return elements;
    }

    private static readonly HashSet<string> BlockTags =
    [
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "table", "section",
        "article", "blockquote", "pre", "center", "header", "footer", "main", "aside", "nav", "form"
    ];

    private static readonly string BlockSelector = string.Join(",", BlockTags);

    private static void WalkNode(INode container, List<DocumentElement> elements)
    {
        var inline = new StringBuilder();

        foreach (var child in container.ChildNodes)
        {
            if (child is IElement element)
            {
                if (IsHidden(element))
                    continue;

                var tag = TagOf(element);
                if (tag == "br")
                {
                    inline.Append(' ');
                }
                else if (BlockTags.Contains(tag))
                {
                    Flush(inline, elements);
                    WalkBlock(element, tag, elements);
                }
                else if (element.QuerySelector(BlockSelector) is not null)
                {
                    Flush(inline, elements);
                    WalkNode(element, elements);
                }
                else
                {
                    inline.Append(InlineText(element));
                }
            }
            else if (child.NodeType == NodeType.Text)
            {
                inline.Append(child.TextContent);
            }
        }

        Flush(inline, elements);
    }

    private static void WalkBlock(IElement element, string tag, List<DocumentElement> elements)
    {
        if (tag == "table")
        {
            var tableText = ExtractTableText(element);
            if (!string.IsNullOrWhiteSpace(tableText))
                elements.Add(new DocumentElement { Type = ElementType.Table, Text = tableText, Table = ExtractGrid(element) });
        }
        else if (tag.Length == 2 && tag[0] == 'h' && char.IsDigit(tag[1]))
        {
            var headingText = CleanText(InlineText(element));
            if (headingText.Length > 0)
                elements.Add(new DocumentElement { Type = ElementType.Heading, Text = headingText });
        }
        else if (element.QuerySelector(BlockSelector) is not null)
        {
            WalkNode(element, elements);
        }
        else
        {
            var text = CleanText(InlineText(element));
            if (text.Length > 0)
                elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = text });
        }
    }

    private static void Flush(StringBuilder inline, List<DocumentElement> elements)
    {
        var text = CleanText(inline.ToString());
        inline.Clear();
        if (text.Length > 0)
            elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = text });
    }

    private static string TagOf(IElement element) => element.TagName.ToLowerInvariant();

    // iXBRL filings open with a hidden ix:header block that is metadata, not prose.
    private static bool IsHidden(IElement element)
    {
        if (TagOf(element) == "ix:header")
            return true;
        var style = element.GetAttribute("style");
        return style is not null && style.Replace(" ", "").Contains("display:none", StringComparison.OrdinalIgnoreCase);
    }

    private static string InlineText(INode node)
    {
        var sb = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            if (child is IElement element)
            {
                if (IsHidden(element))
                    continue;
                if (TagOf(element) == "br")
                    sb.Append(' ');
                else
                    sb.Append(InlineText(element));
            }
            else if (child.NodeType == NodeType.Text)
            {
                sb.Append(child.TextContent);
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<IElement> OwnRows(IElement table) =>
        table.QuerySelectorAll("tr").Where(r => r.Closest("table") == table);

    private static IEnumerable<IElement> CellsOf(IElement row) =>
        row.Children.Where(c => TagOf(c) is "td" or "th");

    private static string ExtractTableText(IElement table)
    {
        var lines = new List<string>();
        foreach (var row in OwnRows(table))
        {
            var cells = CellsOf(row)
                .Select(cell => CleanText(InlineText(cell)))
                .Where(text => text.Length > 0);
            var line = string.Join(" | ", cells);
            if (line.Length > 0)
                lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    private static TableGrid ExtractGrid(IElement table) => new()
    {
        Rows = OwnRows(table)
            .Select(row => new TableRow
            {
                Cells = CellsOf(row)
                    .Select(cell => new TableCell { Text = CleanText(InlineText(cell)), ColSpan = SpanOf(cell, "colspan"), RowSpan = SpanOf(cell, "rowspan") })
                    .ToList()
            })
            .ToList()
    };

    private static int SpanOf(IElement cell, string attribute) =>
        int.TryParse(cell.GetAttribute(attribute), out var span) && span > 0 ? Math.Min(span, MaxSpan) : 1;

    // AngleSharp has already decoded entities; \s also matches U+00A0.
    private static string CleanText(string raw) => Regex.Replace(raw, @"\s+", " ").Trim();
}
