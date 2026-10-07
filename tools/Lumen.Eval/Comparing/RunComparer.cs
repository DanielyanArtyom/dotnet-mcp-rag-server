namespace Lumen.Eval.Comparing;

public class ComparisonReport
{
    public bool EvalSetMismatch { get; set; }
    public bool LabelableMismatch { get; set; }
    public bool ModeMismatch { get; set; }
    public bool ContextTopKMismatch { get; set; }
    public int QuestionsCompared { get; set; }
    public List<string> AccuracyAOnly { get; set; } = [];
    public List<string> AccuracyBOnly { get; set; } = [];
    public double? AccuracyPValue { get; set; }
    public List<string> Hit5AOnly { get; set; } = [];
    public List<string> Hit5BOnly { get; set; } = [];
    public double Hit5PValue { get; set; }
    public double MrrA { get; set; }
    public double MrrB { get; set; }
    public double MrrPValue { get; set; }
}

public static class RunComparer
{
    public static ComparisonReport Compare(RunHeader headerA, IReadOnlyList<QuestionResult> a, RunHeader headerB, IReadOnlyList<QuestionResult> b)
    {
        var collapsedA = Collapse(a);
        var collapsedB = Collapse(b);
        var shared = collapsedA.Keys.Intersect(collapsedB.Keys).Order().ToList();
        var retrievalShared = shared
            .Where(id => collapsedA[id].Labelable && collapsedB[id].Labelable && collapsedA[id].ExpectedKind != ExpectedKind.Refusal)
            .ToList();
        var labelableA = collapsedA.Values.Where(r => r.Labelable).Select(r => r.Id).ToHashSet();
        var labelableB = collapsedB.Values.Where(r => r.Labelable).Select(r => r.Id).ToHashSet();
        var bothFull = headerA.Mode == RunMode.Full && headerB.Mode == RunMode.Full;

        var report = new ComparisonReport
        {
            EvalSetMismatch = headerA.EvalSetSha256 != headerB.EvalSetSha256,
            LabelableMismatch = !labelableA.SetEquals(labelableB),
            ModeMismatch = headerA.Mode != headerB.Mode,
            ContextTopKMismatch = bothFull && headerA.ContextTopK() is { } topKA && headerB.ContextTopK() is { } topKB && topKA != topKB,
            QuestionsCompared = bothFull ? shared.Count : retrievalShared.Count,
            Hit5AOnly = retrievalShared.Where(id => collapsedA[id].FirstHitRank <= 5 && !(collapsedB[id].FirstHitRank <= 5)).ToList(),
            Hit5BOnly = retrievalShared.Where(id => !(collapsedA[id].FirstHitRank <= 5) && collapsedB[id].FirstHitRank <= 5).ToList()
        };

        report.Hit5PValue = Significance.McNemarExact(report.Hit5AOnly.Count, report.Hit5BOnly.Count);

        var rrA = retrievalShared.Select(id => RetrievalMetrics.ReciprocalRank(collapsedA[id].FirstHitRank)).ToList();
        var rrB = retrievalShared.Select(id => RetrievalMetrics.ReciprocalRank(collapsedB[id].FirstHitRank)).ToList();
        report.MrrA = rrA.Count == 0 ? 0 : rrA.Average();
        report.MrrB = rrB.Count == 0 ? 0 : rrB.Average();
        report.MrrPValue = Significance.PairedRandomization(rrA, rrB);

        if (bothFull)
        {
            report.AccuracyAOnly = shared.Where(id => collapsedA[id].Correct == true && collapsedB[id].Correct != true).ToList();
            report.AccuracyBOnly = shared.Where(id => collapsedA[id].Correct != true && collapsedB[id].Correct == true).ToList();
            report.AccuracyPValue = Significance.McNemarExact(report.AccuracyAOnly.Count, report.AccuracyBOnly.Count);
        }

        return report;
    }

    private static Dictionary<string, QuestionResult> Collapse(IReadOnlyList<QuestionResult> results) =>
        results.GroupBy(r => r.Id).ToDictionary(g => g.Key, g =>
        {
            var first = g.OrderBy(r => r.Repeat).First();
            var correctVotes = g.Count(r => r.Correct == true);
            return new QuestionResult
            {
                Id = first.Id,
                Category = first.Category,
                Tenant = first.Tenant,
                ExpectedKind = first.ExpectedKind,
                Labelable = first.Labelable,
                FirstHitRank = first.FirstHitRank,
                Correct = first.Correct is null ? null : correctVotes * 2 > g.Count()
            };
        });
}
