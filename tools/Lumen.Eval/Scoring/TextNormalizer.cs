namespace Lumen.Eval.Scoring;

public static class TextNormalizer
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                '‘' or '’' or 'ʼ' => '\'',
                '“' or '”' => '"',
                '‐' or '‑' or '‒' or '–' or '—' or '−' => '-',
                '|' => ' ',
                _ when char.IsWhiteSpace(ch) => ' ',
                _ => char.ToLowerInvariant(ch)
            });
        }

        return Whitespace.Replace(builder.ToString(), " ").Trim();
    }
}
