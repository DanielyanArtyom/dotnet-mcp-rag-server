namespace Lumen.Eval.Tests.Running;

public class RunStoreTests
{
    [Fact]
    public void Writes_and_reloads_a_run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runs-{Guid.NewGuid():N}");
        var outcome = new RunOutcome
        {
            Header = new RunHeader
            {
                Label = "baseline", Mode = RunMode.Retrieval, StartedAt = DateTimeOffset.Now, EvalSetPath = "eval-set.json",
                EvalSetSha256 = new string('a', 64), Repeat = 1, ServerSettings = JsonDocument.Parse("""{"searchMode":"vector"}""").RootElement
            },
            Results = [new QuestionResult { Id = "q1", Category = "table", Tenant = "acme", Labelable = true, FirstHitRank = 2 }]
        };

        var directory = RunStore.Write(root, outcome);
        var (header, results) = RunStore.Load(directory);

        Assert.EndsWith("_baseline", directory);
        Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
        Assert.Equal("vector", header.ServerSettings.GetProperty("searchMode").GetString());
        Assert.Equal(2, Assert.Single(results).FirstHitRank);
    }
}
