namespace Lumen.Application.Ask;

public record CitationParseResult(IReadOnlySet<int> Cited, IReadOnlyList<int> Unknown);

public static class CitationParser
{
    private static readonly Regex Marker = new(@"\[([0-9]{1,3}(?:\s*,\s*[0-9]{1,3})*)\]", RegexOptions.Compiled);

    public static CitationParseResult Parse(string answer, int sourceCount)
    {
        var cited = new HashSet<int>();
        var unknown = new SortedSet<int>();

        foreach (Match match in Marker.Matches(answer))
        {
            foreach (var part in match.Groups[1].Value.Split(','))
            {
                var id = int.Parse(part.Trim());
                if (id >= 1 && id <= sourceCount)
                {
                    cited.Add(id);
                }
                else
                {
                    unknown.Add(id);
                }
            }
        }

        return new CitationParseResult(cited, unknown.ToList());
    }
}
