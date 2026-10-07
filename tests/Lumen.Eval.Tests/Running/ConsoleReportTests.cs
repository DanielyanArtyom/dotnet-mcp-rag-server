namespace Lumen.Eval.Tests.Running;

public class ConsoleReportTests
{
    private static RunHeader Header(RunMode mode, string settings) => new()
    {
        Label = "x", Mode = mode, EvalSetPath = "set.json", EvalSetSha256 = new string('a', 64), Repeat = 1,
        ServerSettings = JsonDocument.Parse(settings).RootElement
    };

    private static QuestionResult Result(ExpectedKind kind, bool labelable) => new()
    {
        Id = "q1", Category = "c", Tenant = "acme", ExpectedKind = kind, Labelable = labelable, Correct = true, Refused = kind == ExpectedKind.Refusal
    };

    private static string Print(RunHeader header, params QuestionResult[] results)
    {
        var writer = new StringWriter();
        ConsoleReport.PrintRun(writer, header, results, RunSummaryBuilder.Build(results, header.Mode, null), "dir");
        return writer.ToString();
    }

    [Fact]
    public void Full_runs_print_hit_at_k_only_up_to_the_server_context_top_k()
    {
        var output = Print(Header(RunMode.Full, """{"chat":{"deployment":"d","contextTopK":5}}"""), Result(ExpectedKind.Number, true));

        Assert.Contains("hit@5", output);
        Assert.DoesNotContain("hit@10", output);
    }

    [Fact]
    public void Full_runs_with_unreadable_settings_and_retrieval_runs_print_every_k()
    {
        var unreadable = Print(Header(RunMode.Full, "{}"), Result(ExpectedKind.Number, true));
        var retrieval = Print(Header(RunMode.Retrieval, """{"chat":{"contextTopK":3}}"""), Result(ExpectedKind.Number, true));

        Assert.Contains("hit@10", unreadable);
        Assert.Contains("hit@10", retrieval);
    }

    [Fact]
    public void Refusal_rows_are_not_labelled_unlabelable()
    {
        var refusal = Print(Header(RunMode.Full, "{}"), Result(ExpectedKind.Refusal, false));
        var number = Print(Header(RunMode.Full, "{}"), Result(ExpectedKind.Number, false));

        Assert.DoesNotContain("  unlabelable", RowsOnly(refusal));
        Assert.Contains("  unlabelable", RowsOnly(number));
    }

    [Fact]
    public void The_header_prints_the_ingest_profile_and_the_corpus()
    {
        var header = Header(RunMode.Retrieval, "{}");
        header.IngestProfile = "p1";
        header.Corpus = [new CorpusDocument("acme", "Own", "p1", 1)];

        var output = Print(header, Result(ExpectedKind.Number, true));

        Assert.Contains("ingest profile: p1", output);
        Assert.Contains(output.Split('\n'), l => l.Contains("Own") && l.Contains("1 chunks"));
    }

    private static string RowsOnly(string output) =>
        string.Join('\n', output.Split('\n').Where(l => l.StartsWith("q1")));
}
