using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Lumen.Infrastructure.DocumentReading;

public class PdfDocumentReader : IDocumentReader
{
    private const double LineTolerance = 2.0;
    private const int MinTableColumns = 3;

    // PdfDocument.Open takes and disposes the stream and needs it seekable: pass a fresh MemoryStream.
    public Task<List<DocumentElement>> ReadAsync(Stream fileStream, CancellationToken ct = default)
    {
        var elements = new List<DocumentElement>();
        using var document = PdfDocument.Open(fileStream);

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            var currentTable = new List<List<string>>();
            foreach (var line in GroupWordsIntoLines(page.GetWords().ToList()))
            {
                var cells = SplitIntoCells(line);
                if (cells.Count >= MinTableColumns)
                {
                    currentTable.Add(cells);
                    continue;
                }

                FlushTable(currentTable, elements);

                var text = string.Join(" ", line.Select(w => w.Text));
                if (!string.IsNullOrWhiteSpace(text))
                    elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = text });
            }

            FlushTable(currentTable, elements);
        }

        return Task.FromResult(elements);
    }

    private static void FlushTable(List<List<string>> rows, List<DocumentElement> elements)
    {
        if (rows.Count == 0)
            return;

        var tableText = string.Join("\n", rows.Select(row => string.Join(" | ", row)));
        elements.Add(new DocumentElement { Type = ElementType.Table, Text = tableText });
        rows.Clear();
    }

    // First-pass heuristic, not a verified technique (PdfPig has no table API): a line is a table
    // row when its words split into 3+ cells at gaps wider than ~1.5x the word height. Limits:
    // prose with wide justified gaps, two-column layouts and 2-column tables are misclassified,
    // ruled tables without wide gaps are missed, and consecutive table rows form one Table
    // element (tables are split only by prose lines). Revisit against real filings.
    private static List<List<Word>> GroupWordsIntoLines(List<Word> words)
    {
        var lines = new List<List<Word>>();

        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Top))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Top - word.BoundingBox.Top) < LineTolerance);
            if (line is null)
            {
                line = [];
                lines.Add(line);
            }
            line.Add(word);
        }

        foreach (var line in lines)
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

        return lines;
    }

    private static List<string> SplitIntoCells(List<Word> line)
    {
        var cellGap = line.Max(w => w.BoundingBox.Height) * 1.5;
        var cells = new List<string>();
        var current = new List<string> { line[0].Text };

        for (var i = 1; i < line.Count; i++)
        {
            if (line[i].BoundingBox.Left - line[i - 1].BoundingBox.Right > cellGap)
            {
                cells.Add(string.Join(" ", current));
                current.Clear();
            }
            current.Add(line[i].Text);
        }
        cells.Add(string.Join(" ", current));

        return cells;
    }
}
