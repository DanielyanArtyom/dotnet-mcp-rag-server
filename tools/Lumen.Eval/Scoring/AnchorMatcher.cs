namespace Lumen.Eval.Scoring;

public static class AnchorMatcher
{
    public static bool IsRelevant(string chunkText, List<List<string>> anchors)
    {
        if (anchors.Count == 0)
        {
            return false;
        }

        var normalized = TextNormalizer.Normalize(chunkText);

        return anchors.Any(group => group.Count > 0
            && group.All(anchor => normalized.Contains(TextNormalizer.Normalize(anchor), StringComparison.Ordinal)));
    }
}
