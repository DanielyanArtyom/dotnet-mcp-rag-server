namespace Lumen.Eval.Running;

public class RetrievalAnswerGrid
{
    public int HitCorrect { get; set; }
    public int HitWrong { get; set; }
    public int MissCorrect { get; set; }
    public int MissWrong { get; set; }
}

public class CategorySummary
{
    public string Category { get; set; } = default!;
    public int Count { get; set; }
    public double? HitAt5 { get; set; }
    public double? Mrr { get; set; }
    public double? Accuracy { get; set; }
}

public class RunSummary
{
    public int Rows { get; set; }
    public int RetrievalQuestions { get; set; }
    public double HitAt1 { get; set; }
    public double HitAt3 { get; set; }
    public double HitAt5 { get; set; }
    public double HitAt10 { get; set; }
    public double Mrr { get; set; }
    public double MeanRetrievedTokens { get; set; }
    public double? Accuracy { get; set; }
    public double? FalseAnswerRate { get; set; }
    public double? FalseRefusalRate { get; set; }
    public int TruncatedCount { get; set; }
    public RetrievalAnswerGrid Grid { get; set; } = new();
    public double LatencyP50 { get; set; }
    public double LatencyP95 { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public double? RepeatFlipRate { get; set; }
    public List<CategorySummary> ByCategory { get; set; } = [];
}

public static class RunSummaryBuilder
{
    public static RunSummary Build(IReadOnlyList<QuestionResult> results, RunMode mode, ModelPrice? price)
    {
        var retrieval = results.Where(r => r.Labelable && r.ExpectedKind != ExpectedKind.Refusal).ToList();
        var ranks = retrieval.Select(r => r.FirstHitRank).ToList();
        var full = mode == RunMode.Full;

        var summary = new RunSummary
        {
            Rows = results.Count,
            RetrievalQuestions = retrieval.Count,
            HitAt1 = RetrievalMetrics.HitAt(ranks, 1),
            HitAt3 = RetrievalMetrics.HitAt(ranks, 3),
            HitAt5 = RetrievalMetrics.HitAt(ranks, 5),
            HitAt10 = RetrievalMetrics.HitAt(ranks, 10),
            Mrr = RetrievalMetrics.Mrr(ranks),
            MeanRetrievedTokens = results.Count == 0 ? 0 : results.Average(r => r.RetrievedTokens),
            LatencyP50 = RetrievalMetrics.Percentile(results.Select(r => (double)r.LatencyMs).ToList(), 50),
            LatencyP95 = RetrievalMetrics.Percentile(results.Select(r => (double)r.LatencyMs).ToList(), 95),
            InputTokens = results.Sum(r => (long)r.InputTokens),
            OutputTokens = results.Sum(r => (long)r.OutputTokens),
            ReasoningTokens = results.Sum(r => (long)r.ReasoningTokens),
            TruncatedCount = results.Count(r => r.Truncated == true)
        };

        if (full)
        {
            summary.Accuracy = Rate(results, r => r.Correct == true);
            summary.FalseAnswerRate = Rate(results.Where(r => r.ExpectedKind == ExpectedKind.Refusal).ToList(), r => r.Refused != true);
            summary.FalseRefusalRate = Rate(results.Where(r => r.ExpectedKind != ExpectedKind.Refusal).ToList(), r => r.Refused == true);

            foreach (var r in retrieval)
            {
                var hit = r.FirstHitRank is not null;
                var correct = r.Correct == true;
                if (hit && correct) summary.Grid.HitCorrect++;
                else if (hit) summary.Grid.HitWrong++;
                else if (correct) summary.Grid.MissCorrect++;
                else summary.Grid.MissWrong++;
            }

            if (price is not null)
            {
                summary.CostUsd = (summary.InputTokens * price.InputPerMillion + summary.OutputTokens * price.OutputPerMillion) / 1_000_000m;
            }

            var byQuestion = results.GroupBy(r => r.Id).Where(g => g.Count() > 1).ToList();
            if (byQuestion.Count > 0)
            {
                summary.RepeatFlipRate = byQuestion.Count(g => g.Select(r => r.Correct).Distinct().Count() > 1) / (double)byQuestion.Count;
            }
        }

        summary.ByCategory = results.GroupBy(r => r.Category).OrderBy(g => g.Key).Select(g =>
        {
            var categoryRanks = g.Where(r => r.Labelable && r.ExpectedKind != ExpectedKind.Refusal).Select(r => r.FirstHitRank).ToList();
            return new CategorySummary
            {
                Category = g.Key,
                Count = g.Count(),
                HitAt5 = categoryRanks.Count == 0 ? null : RetrievalMetrics.HitAt(categoryRanks, 5),
                Mrr = categoryRanks.Count == 0 ? null : RetrievalMetrics.Mrr(categoryRanks),
                Accuracy = full ? Rate(g.ToList(), r => r.Correct == true) : null
            };
        }).ToList();

        return summary;
    }

    private static double? Rate(IReadOnlyCollection<QuestionResult> rows, Func<QuestionResult, bool> predicate) =>
        rows.Count == 0 ? null : rows.Count(predicate) / (double)rows.Count;
}
