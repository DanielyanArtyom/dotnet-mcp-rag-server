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
                EvalSetSha256 = new string('a', 64), Repeat = 1, ServerSettings = JsonDocument.Parse("""{"searchMode":"vector"}""").RootElement,
                IngestProfile = "p1", Corpus = [new CorpusDocument("acme", "Own", "p1", 3)]
            },
            Results = [new QuestionResult { Id = "q1", Category = "table", Tenant = "acme", Labelable = true, FirstHitRank = 2, Sources = [new SourceRecord(1, Guid.NewGuid(), Guid.NewGuid(), 4, "Text", 0.2, 0.03, "preview")] }]
        };

        var directory = RunStore.Write(root, outcome);
        var (header, results) = RunStore.Load(directory);

        Assert.EndsWith("_baseline", directory);
        Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
        Assert.Equal("vector", header.ServerSettings.GetProperty("searchMode").GetString());
        var result = Assert.Single(results);
        Assert.Equal(2, result.FirstHitRank);
        var source = Assert.Single(result.Sources);
        Assert.Equal(("preview", 0.03, 4), (source.TextPreview, source.Score, source.ChunkIndex));
        Assert.Equal("p1", header.IngestProfile);
        Assert.Equal(new CorpusDocument("acme", "Own", "p1", 3), Assert.Single(header.Corpus));
    }
}
