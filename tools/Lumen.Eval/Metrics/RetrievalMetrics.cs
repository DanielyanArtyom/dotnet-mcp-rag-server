namespace Lumen.Eval.Metrics;

public static class RetrievalMetrics
{
    public static int? FirstHitRank(IReadOnlyList<string> retrievedTexts, List<List<string>> anchors)
    {
        for (var i = 0; i < retrievedTexts.Count; i++)
        {
            if (AnchorMatcher.IsRelevant(retrievedTexts[i], anchors))
            {
                return i + 1;
            }
        }

        return null;
    }

    public static double HitAt(IReadOnlyCollection<int?> ranks, int k) =>
        ranks.Count == 0 ? 0 : ranks.Count(r => r <= k) / (double)ranks.Count;

    public static double ReciprocalRank(int? rank) => rank is { } r ? 1.0 / r : 0.0;

    public static double Mrr(IReadOnlyCollection<int?> ranks) =>
        ranks.Count == 0 ? 0 : ranks.Average(ReciprocalRank);

    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var index = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
