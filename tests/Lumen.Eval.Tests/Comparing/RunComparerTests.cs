using Lumen.Eval.Comparing;

namespace Lumen.Eval.Tests.Comparing;

public class RunComparerTests
{
    private static RunHeader Header(RunMode mode, string sha = "aaa") => new() { Label = "x", Mode = mode, EvalSetSha256 = sha };

    private static RunHeader HeaderWithTopK(RunMode mode, int? contextTopK)
    {
        var header = Header(mode);
        if (contextTopK is { } k)
        {
            header.ServerSettings = JsonDocument.Parse("{\"chat\":{\"contextTopK\":" + k + "}}").RootElement;
        }

        return header;
    }

    private static QuestionResult R(string id, int? rank, bool? correct, int repeat = 0) => new()
    {
        Id = id, Category = "c", Tenant = "acme", ExpectedKind = ExpectedKind.Number, Labelable = true,
        FirstHitRank = rank, Correct = correct, Repeat = repeat
    };

    [Fact]
    public void Counts_flips_in_both_directions_and_tests_them()
    {
        List<QuestionResult> a = [R("q1", 1, true), R("q2", null, false), R("q3", null, false), R("q4", 1, true)];
        List<QuestionResult> b = [R("q1", 1, true), R("q2", 1, true), R("q3", 2, true), R("q4", 1, false)];

        var report = RunComparer.Compare(Header(RunMode.Full), a, Header(RunMode.Full), b);

        Assert.Equal(4, report.QuestionsCompared);
        Assert.Equal(new[] { "q4" }, report.AccuracyAOnly);
        Assert.Equal(new[] { "q2", "q3" }, report.AccuracyBOnly);
        Assert.Equal(Significance.McNemarExact(1, 2), report.AccuracyPValue);
        Assert.Equal(new[] { "q2", "q3" }, report.Hit5BOnly);
        Assert.False(report.EvalSetMismatch);
        Assert.False(report.LabelableMismatch);
    }

    [Fact]
    public void Retrieval_runs_compare_without_accuracy()
    {
        var report = RunComparer.Compare(Header(RunMode.Retrieval), [R("q1", 3, null)], Header(RunMode.Retrieval), [R("q1", 1, null)]);

        Assert.Null(report.AccuracyPValue);
        Assert.Equal(1 / 3.0, report.MrrA, precision: 10);
        Assert.Equal(1.0, report.MrrB, precision: 10);
    }

    [Fact]
    public void Warns_about_different_eval_sets_and_compares_only_shared_labelable_questions()
    {
        var unlabelable = R("q2", null, null);
        unlabelable.Labelable = false;

        var report = RunComparer.Compare(Header(RunMode.Retrieval, "aaa"), [R("q1", 1, null), unlabelable], Header(RunMode.Retrieval, "bbb"), [R("q1", 1, null), R("q2", 1, null)]);

        Assert.True(report.EvalSetMismatch);
        Assert.True(report.LabelableMismatch);
        Assert.Equal(1, report.QuestionsCompared);
    }

    [Fact]
    public void Repeats_collapse_to_the_majority_verdict()
    {
        List<QuestionResult> a = [R("q1", 1, true, 0), R("q1", 1, true, 1), R("q1", 1, false, 2)];
        List<QuestionResult> b = [R("q1", 1, false, 0), R("q1", 1, false, 1), R("q1", 1, true, 2)];

        var report = RunComparer.Compare(Header(RunMode.Full), a, Header(RunMode.Full), b);

        Assert.Equal(new[] { "q1" }, report.AccuracyAOnly);
    }

    [Fact]
    public void Flags_a_retrieval_versus_full_comparison()
    {
        var report = RunComparer.Compare(Header(RunMode.Retrieval), [R("q1", 1, null)], Header(RunMode.Full), [R("q1", 1, true)]);

        Assert.True(report.ModeMismatch);
    }

    [Fact]
    public void Flags_two_full_runs_with_different_context_top_k()
    {
        var report = RunComparer.Compare(HeaderWithTopK(RunMode.Full, 5), [R("q1", 1, true)], HeaderWithTopK(RunMode.Full, 8), [R("q1", 1, true)]);

        Assert.False(report.ModeMismatch);
        Assert.True(report.ContextTopKMismatch);
    }

    [Fact]
    public void Does_not_flag_equal_or_unreadable_context_top_k()
    {
        var same = RunComparer.Compare(HeaderWithTopK(RunMode.Full, 5), [R("q1", 1, true)], HeaderWithTopK(RunMode.Full, 5), [R("q1", 1, true)]);
        var missing = RunComparer.Compare(HeaderWithTopK(RunMode.Full, null), [R("q1", 1, true)], HeaderWithTopK(RunMode.Full, 5), [R("q1", 1, true)]);

        Assert.False(same.ContextTopKMismatch);
        Assert.False(missing.ContextTopKMismatch);
        Assert.False(missing.ModeMismatch);
    }
}
