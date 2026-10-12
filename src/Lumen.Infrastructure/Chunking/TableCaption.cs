namespace Lumen.Infrastructure.Chunking;

public static class TableCaption
{
    public const string Prefix = "Table: ";
    public const int MaxWords = 40;

    private static readonly Regex Marker = new("following table|as follows|summar", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])(?<!\b(?:[A-Z]|Inc|Co|No|Corp)\.)\s+(?=[A-Z(“""])", RegexOptions.Compiled);

    public static string? Find(DocumentElement? previous)
    {
        if (previous is not { Type: ElementType.Paragraph } || string.IsNullOrWhiteSpace(previous.Text))
        {
            return null;
        }

        var sentences = SentenceBreak.Split(previous.Text.Trim());
        var sentence = sentences.LastOrDefault(s => Marker.IsMatch(s)) ?? (sentences[^1].EndsWith(':') ? sentences[^1] : null);
        if (sentence is null)
        {
            return null;
        }

        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= MaxWords ? sentence : string.Join(' ', words.Take(MaxWords)) + " …";
    }
}
