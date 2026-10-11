namespace Lumen.Eval.Tests.Running;

public class RunSummaryBuilderTests
{
    private static QuestionResult R(string id, string category, ExpectedKind kind, int? rank, bool? correct, bool refused = false, int repeat = 0, long ms = 100) => new()
    {
        Id = id, Category = category, Tenant = "acme", ExpectedKind = kind, Repeat = repeat,
        Labelable = kind != ExpectedKind.Refusal, FirstHitRank = rank, Correct = correct, Refused = refused,
        LatencyMs = ms, InputTokens = 1000, OutputTokens = 100, RetrievedTokens = 1250
    };

    [Fact]
    public void Aggregates_retrieval_answer_and_refusal_metrics()
    {
        List<QuestionResult> results =
        [
            R("q1", "table", ExpectedKind.Number, 1, true),
            R("q2", "table", ExpectedKind.Number, null, false),
            R("q3", "prose", ExpectedKind.Text, 3, true),
            R("q4", "unanswerable", ExpectedKind.Refusal, null, false, refused: false),
            R("q5", "prose", ExpectedKind.Text, 2, false, refused: true)
        ];

        var summary = RunSummaryBuilder.Build(results, RunMode.Full, new ModelPrice { InputPerMillion = 2m, OutputPerMillion = 10m });

        Assert.Equal(4, summary.RetrievalQuestions);
        Assert.Equal(0.25, summary.HitAt1);
        Assert.Equal(0.75, summary.HitAt5);
        Assert.Equal(2 / 5.0, summary.Accuracy);
        Assert.Equal(1.0, summary.FalseAnswerRate);
        Assert.Equal(0.25, summary.FalseRefusalRate);
        Assert.Equal(2, summary.Grid.HitCorrect);
        Assert.Equal(1, summary.Grid.HitWrong);
        Assert.Equal(0, summary.Grid.MissCorrect);
        Assert.Equal(1, summary.Grid.MissWrong);
        Assert.Equal(5 * (1000 * 2m + 100 * 10m) / 1_000_000m, summary.CostUsd);
        Assert.Contains(summary.ByCategory, c => c.Category == "table" && c.Count == 2 && c.Accuracy == 0.5);
    }

    [Fact]
    public void Retrieval_runs_have_no_answer_metrics()
    {
        var summary = RunSummaryBuilder.Build([R("q1", "table", ExpectedKind.Number, 2, null)], RunMode.Retrieval, null);

        Assert.Null(summary.Accuracy);
        Assert.Null(summary.CostUsd);
        Assert.Equal(0.5, summary.Mrr);
    }

    [Fact]
    public void Repeats_report_the_share_of_questions_whose_verdict_flipped()
    {
        List<QuestionResult> results =
        [
            R("q1", "table", ExpectedKind.Number, 1, true, repeat: 0),
            R("q1", "table", ExpectedKind.Number, 1, false, repeat: 1),
            R("q2", "table", ExpectedKind.Number, 1, true, repeat: 0),
            R("q2", "table", ExpectedKind.Number, 1, true, repeat: 1)
        ];

        Assert.Equal(0.5, RunSummaryBuilder.Build(results, RunMode.Full, null).RepeatFlipRate);
    }

    [Fact]
    public void Unlabelable_questions_are_excluded_from_retrieval_metrics()
    {
        var unlabelable = R("q1", "table", ExpectedKind.Number, null, true);
        unlabelable.Labelable = false;

        var summary = RunSummaryBuilder.Build([unlabelable, R("q2", "table", ExpectedKind.Number, 1, true)], RunMode.Full, null);

        Assert.Equal(1, summary.RetrievalQuestions);
        Assert.Equal(1.0, summary.HitAt1);
    }

    [Fact]
    public void Agent_runs_report_accuracy_and_use_retrieved_relevant_for_the_grid()
    {
        var results = new List<QuestionResult>
        {
            new() { Id = "a", Category = "c", ExpectedKind = ExpectedKind.Number, Labelable = true, RetrievedRelevant = true, Correct = true, Refused = false, ToolCalls = ["s", "s"] },
            new() { Id = "b", Category = "c", ExpectedKind = ExpectedKind.Number, Labelable = true, RetrievedRelevant = false, Correct = false, Refused = true, ToolCalls = ["s"] },
            new() { Id = "c", Category = "c", ExpectedKind = ExpectedKind.Refusal, Correct = true, Refused = true, ToolCalls = [] }
        };

        var summary = RunSummaryBuilder.Build(results, RunMode.Agent, null);

        Assert.Equal(2 / 3.0, summary.Accuracy!.Value, 3);
        Assert.Equal(0.0, summary.FalseAnswerRate);
        Assert.Equal(0.5, summary.FalseRefusalRate);
        Assert.Equal(0.5, summary.RetrievedRelevantRate);
        Assert.Equal(1.0, summary.MeanToolCalls);
        Assert.Equal(1, summary.Grid.HitCorrect);
        Assert.Equal(1, summary.Grid.MissWrong);
    }

    [Fact]
    public void Agent_cost_is_the_sum_of_the_cli_reported_costs()
    {
        var results = new List<QuestionResult>
        {
            new() { Id = "a", Category = "c", ExpectedKind = ExpectedKind.Refusal, Correct = true, Refused = true, AgentCostUsd = 0.01m },
            new() { Id = "b", Category = "c", ExpectedKind = ExpectedKind.Refusal, Correct = true, Refused = true, AgentCostUsd = 0.02m }
        };

        Assert.Equal(0.03m, RunSummaryBuilder.Build(results, RunMode.Agent, null).CostUsd);
    }

    [Fact]
    public void Agent_runs_report_no_ranking_metrics_and_a_per_category_relevant_seen_rate()
    {
        var results = new List<QuestionResult>
        {
            new() { Id = "a", Category = "c", ExpectedKind = ExpectedKind.Number, Labelable = true, RetrievedRelevant = true, Correct = true },
            new() { Id = "b", Category = "c", ExpectedKind = ExpectedKind.Number, Labelable = true, RetrievedRelevant = false, Correct = false }
        };

        var summary = RunSummaryBuilder.Build(results, RunMode.Agent, null);

        Assert.Null(summary.HitAt1);
        Assert.Null(summary.HitAt5);
        Assert.Null(summary.HitAt10);
        Assert.Null(summary.Mrr);
        var category = Assert.Single(summary.ByCategory);
        Assert.Null(category.HitAt5);
        Assert.Null(category.Mrr);
        Assert.Equal(0.5, category.RetrievedRelevantRate);
    }
}
