namespace Lumen.Eval.Tests.Metrics;

public class RetrievalMetricsTests
{
    [Fact]
    public void First_hit_rank_is_one_based_and_null_when_nothing_matches()
    {
        List<List<string>> anchors = [["416,161"]];

        Assert.Equal(2, RetrievalMetrics.FirstHitRank(["intro", "Total net sales 416,161", "416,161 again"], anchors));
        Assert.Null(RetrievalMetrics.FirstHitRank(["intro", "other"], anchors));
    }

    [Fact]
    public void Hit_at_k_and_mrr_over_a_set_of_ranks()
    {
        int?[] ranks = [1, 3, null, 2];

        Assert.Equal(0.25, RetrievalMetrics.HitAt(ranks, 1));
        Assert.Equal(0.75, RetrievalMetrics.HitAt(ranks, 3));
        Assert.Equal((1 + 1 / 3.0 + 0 + 0.5) / 4, RetrievalMetrics.Mrr(ranks), precision: 10);
    }

    [Fact]
    public void Empty_rank_sets_score_zero()
    {
        Assert.Equal(0, RetrievalMetrics.HitAt([], 5));
        Assert.Equal(0, RetrievalMetrics.Mrr([]));
    }

    [Fact]
    public void Percentile_uses_nearest_rank()
    {
        double[] values = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

        Assert.Equal(50, RetrievalMetrics.Percentile(values, 50));
        Assert.Equal(100, RetrievalMetrics.Percentile(values, 95));
    }
}
