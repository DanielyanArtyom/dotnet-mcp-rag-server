namespace Lumen.Infrastructure.Chunking;

public static class SectionHeading
{
    private const int MaxWords = 8;
    private const int MaxLength = 80;

    // SEC filings almost never mark headings with <h1>–<h6>; "Gross Margin" or "Capital Return Program"
    // arrive as short standalone paragraphs, so a heading is recognised by shape, not by tag.
    public static bool IsHeadingLike(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is 0 or > MaxLength || trimmed.Contains('|'))
        {
            return false;
        }

        if (!char.IsLetterOrDigit(trimmed[0]) || !trimmed.Any(char.IsLetter) || trimmed[^1] is '.' or ':' or ';' or ',')
        {
            return false;
        }

        return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxWords;
    }
}
