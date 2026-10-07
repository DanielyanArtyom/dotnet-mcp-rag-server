namespace Lumen.Eval.Running;

public static class ConsoleReport
{
    public static void PrintRun(TextWriter output, RunHeader header, IReadOnlyList<QuestionResult> results, RunSummary summary, string runDirectory)
    {
        output.WriteLine("=== Lumen eval run ===");
        output.WriteLine($"label: {header.Label}   mode: {header.Mode}   repeat: {header.Repeat}   holdout: {header.Holdout}");
        output.WriteLine($"started: {header.StartedAt:yyyy-MM-dd HH:mm:ss}   git: {header.GitCommit}");
        output.WriteLine($"eval set: {header.EvalSetPath}   sha256: {header.EvalSetSha256[..12]}");
        output.WriteLine($"judge deployment: {header.JudgeDeployment ?? "none"}");
        if (header.AbortReason is not null)
        {
            output.WriteLine($"ABORTED (partial run): {header.AbortReason}");
        }

        output.WriteLine("server settings:");
        output.WriteLine(JsonSerializer.Serialize(header.ServerSettings, JsonDefaults.Indented));
        output.WriteLine($"ingest profile: {header.IngestProfile ?? "(none)"}");
        foreach (var d in header.Corpus)
        {
            output.WriteLine($"  {d.Tenant} / {d.Title}: {d.ChunkCount} chunks");
        }

        output.WriteLine($"unlabelable (excluded from retrieval metrics): {Join(header.Unlabelable)}");
        output.WriteLine($"broad anchors (> {EvalRunner.BroadAnchorThreshold} matching chunks): {Join(header.BroadAnchors.Select(kv => $"{kv.Key}={kv.Value}"))}");
        output.WriteLine();

        output.WriteLine($"{"id",-10} {"rep",3} {"category",-16} {"tenant",-7} {"rank",4} {"ok",3} {"ref",3} {"in/out tok",11} {"ms",6}  note");
        foreach (var r in results)
        {
            var ok = r.Correct switch { true => "✓", false => "✗", null => "-" };
            var refused = r.Refused switch { true => "y", false => "n", null => "-" };
            var note = r.Truncated == true ? "TRUNCATED " : string.Empty;
            note += r.Labelable || r.ExpectedKind == ExpectedKind.Refusal ? r.JudgeReason ?? string.Empty : "unlabelable";
            output.WriteLine($"{r.Id,-10} {r.Repeat,3} {r.Category,-16} {r.Tenant,-7} {r.FirstHitRank?.ToString() ?? "-",4} {ok,3} {refused,3} {$"{r.InputTokens}/{r.OutputTokens}",11} {r.LatencyMs,6}  {Truncate(note, 60)}");
        }

        output.WriteLine();
        var contextTopK = header.Mode == RunMode.Full ? header.ContextTopK() : null;
        var hits = new List<string>();
        foreach (var (k, value) in new[] { (1, summary.HitAt1), (3, summary.HitAt3), (5, summary.HitAt5), (10, summary.HitAt10) })
        {
            if (contextTopK is not { } cap || k <= cap)
            {
                hits.Add($"hit@{k} {value:P0}");
            }
        }

        output.WriteLine($"retrieval ({summary.RetrievalQuestions} labelable): {string.Join("  ", hits)}  MRR {summary.Mrr:F3}  mean retrieved tokens {summary.MeanRetrievedTokens:F0}");
        if (contextTopK is { } depth)
        {
            output.WriteLine($"  (full run: ranks come from /api/ask sources, capped at server contextTopK={depth})");
        }

        if (summary.Accuracy is not null)
        {
            output.WriteLine($"answers: accuracy {summary.Accuracy:P0}  false answers {summary.FalseAnswerRate:P0}  false refusals {summary.FalseRefusalRate:P0}  truncated {summary.TruncatedCount}");
            output.WriteLine($"retrieval × answer: hit✓ {summary.Grid.HitCorrect}  hit✗ {summary.Grid.HitWrong}  miss✓ {summary.Grid.MissCorrect}  miss✗ {summary.Grid.MissWrong}");
            output.WriteLine($"tokens: in {summary.InputTokens}  out {summary.OutputTokens} (reasoning {summary.ReasoningTokens})  cost {(summary.CostUsd is { } cost ? $"${cost:F4}" : "n/a (no price configured)")}  judge tokens {header.JudgeInputTokens}/{header.JudgeOutputTokens}");
        }

        if (summary.RepeatFlipRate is { } flip)
        {
            output.WriteLine($"stability: {flip:P0} of questions changed verdict across repeats");
        }

        output.WriteLine($"latency: p50 {summary.LatencyP50:F0} ms  p95 {summary.LatencyP95:F0} ms");
        output.WriteLine();
        output.WriteLine($"{"category",-16} {"n",3} {"hit@5",6} {"MRR",6} {"acc",6}");
        foreach (var c in summary.ByCategory)
        {
            output.WriteLine($"{c.Category,-16} {c.Count,3} {Pct(c.HitAt5),6} {c.Mrr?.ToString("F3") ?? "-",6} {Pct(c.Accuracy),6}");
        }

        output.WriteLine();
        output.WriteLine($"saved to {runDirectory}");
    }

    public static void PrintComparison(TextWriter output, string dirA, string dirB, ComparisonReport report)
    {
        output.WriteLine($"=== compare A={dirA}  B={dirB} ===");
        if (report.EvalSetMismatch)
        {
            output.WriteLine("WARNING: the two runs used different eval sets (sha256 differs) — the comparison is not like-for-like.");
        }

        if (report.LabelableMismatch)
        {
            output.WriteLine("WARNING: the two runs have different labelable-question sets — the retrieval metrics are not like-for-like.");
        }

        if (report.ModeMismatch)
        {
            output.WriteLine("WARNING: one run is retrieval and the other full — retrieval depth differs (topK 10 vs the server's contextTopK), so hit@k and MRR are not like-for-like.");
        }

        if (report.ContextTopKMismatch)
        {
            output.WriteLine("WARNING: the two full runs used different server contextTopK values — retrieval depth differs, so hit@k and MRR are not like-for-like.");
        }

        output.WriteLine($"questions compared: {report.QuestionsCompared}");
        output.WriteLine($"hit@5 flips: A-only {Join(report.Hit5AOnly)} | B-only {Join(report.Hit5BOnly)}   McNemar p={report.Hit5PValue:F4}");
        output.WriteLine($"MRR: A {report.MrrA:F3}  B {report.MrrB:F3}   paired randomization p={report.MrrPValue:F4}");
        if (report.AccuracyPValue is { } p)
        {
            output.WriteLine($"accuracy flips: A-only {Join(report.AccuracyAOnly)} | B-only {Join(report.AccuracyBOnly)}   McNemar p={p:F4}");
        }

        output.WriteLine("Rule of thumb at n≈25: about 6–0 or 8–1 net flips are needed for p < 0.05; a difference of 2 questions is noise.");
    }

    private static string Join(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private static string Pct(double? value) => value is { } v ? v.ToString("P0") : "-";

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
