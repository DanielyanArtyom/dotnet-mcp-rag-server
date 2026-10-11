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

    [Fact]
    public void Agent_runs_print_no_ranking_metrics_and_a_relevant_seen_column()
    {
        var result = Result(ExpectedKind.Number, true);
        result.RetrievedRelevant = true;

        var output = Print(Header(RunMode.Agent, """{"chat":{"deployment":"d"}}"""), result);

        Assert.DoesNotContain("hit@", output);
        Assert.DoesNotContain("MRR", output);
        Assert.Contains("relevant chunk seen 100%", output);
        var categoryHeader = output.Split('\n').Single(l => l.StartsWith("category"));
        Assert.Contains("seen", categoryHeader);
        Assert.Contains(output.Split('\n'), l => l.StartsWith("c ") && l.Contains("100%"));
    }

    [Fact]
    public void Agent_runs_print_both_the_reported_and_the_configured_model()
    {
        var header = Header(RunMode.Agent, """{"chat":{"deployment":"d"}}""");
        header.AgentModel = "claude-sonnet-5-5";
        header.AgentConfiguredModel = "sonnet";

        var output = Print(header, Result(ExpectedKind.Number, true));

        Assert.Contains("agent: model claude-sonnet-5-5 (configured sonnet)", output);
    }

    [Theory]
    [InlineData(RunMode.Retrieval, RunMode.Full, "retrieval depth differs")]
    [InlineData(RunMode.Full, RunMode.Agent, "only answer accuracy is compared")]
    [InlineData(RunMode.Retrieval, RunMode.Agent, "nothing is compared")]
    public void The_mode_mismatch_warning_matches_what_is_actually_compared(RunMode a, RunMode b, string expected)
    {
        var report = RunComparer.Compare(Header(a, "{}"), [Result(ExpectedKind.Number, true)], Header(b, "{}"), [Result(ExpectedKind.Number, true)]);
        var writer = new StringWriter();

        ConsoleReport.PrintComparison(writer, "A", "B", report);

        var warning = writer.ToString().Split('\n').Single(l => l.Contains("different modes"));
        Assert.Contains(expected, warning);
    }
}
