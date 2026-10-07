using System.Net;
using System.Text;
using Lumen.Eval.Api;

namespace Lumen.Eval.Tests.Running;

public class EvalRunnerTests
{
    private static readonly Guid OwnDocument = Guid.NewGuid();
    private static readonly Guid ForeignDocument = Guid.NewGuid();

    private sealed class StubHandler(string answeringDeployment = "gpt-answer", bool foreignHits = true, string? failWhenBodyContains = null, string documentProfile = "p1") : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (failWhenBodyContains is not null && request.Content is not null
                && (await request.Content.ReadAsStringAsync(cancellationToken)).Contains(failWhenBodyContains))
            {
                throw new HttpRequestException("stub failure");
            }

            var path = request.RequestUri!.AbsolutePath;
            var hitDocument = foreignHits ? ForeignDocument : OwnDocument;
            var json = path switch
            {
                "/api/auth/token" => """{"token":"t"}""",
                "/api/settings" => $$$"""{"chat":{"deployment":"{{{answeringDeployment}}}"},"ingestProfile":"p1"}""",
                "/api/documents" => $$$"""[{"id":"{{{OwnDocument}}}","title":"Own","ingestProfile":"{{{documentProfile}}}"}]""",
                _ when path.EndsWith("/chunks") => """[{"chunkIndex":0,"text":"net sales 416,161","tokenCount":5,"chunkType":"Text"}]""",
                "/api/search" => $$$"""[{"chunkId":"{{{Guid.NewGuid()}}}","documentId":"{{{hitDocument}}}","documentTitle":"X","chunkIndex":0,"chunkType":"Text","tokenCount":5,"text":"net sales 416,161","distance":0.1}]""",
                "/api/ask" => $$$"""{"answer":"416,161 [1]","refused":false,"truncated":false,"sources":[{"sourceId":1,"chunkId":"{{{Guid.NewGuid()}}}","documentId":"{{{hitDocument}}}","documentTitle":"X","chunkIndex":0,"chunkType":"Text","tokenCount":5,"distance":0.1,"cited":true,"text":"net sales 416,161"}],"usage":{"inputTokens":1,"outputTokens":1,"reasoningTokens":0},"timings":{"retrievalMs":1,"generationMs":1,"totalMs":2}}""",
                _ => throw new InvalidOperationException($"Unexpected request {path}")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubJudge : IAnswerJudge
    {
        public Task<JudgeVerdict> JudgeAsync(string question, string reference, string candidate, CancellationToken ct) =>
            Task.FromResult(new JudgeVerdict(true, "stub", 0, 0));
    }

    private static EvalSet Set(params string[] extraQuestions) => new()
    {
        Version = 1,
        Questions =
        [
            new EvalQuestion
            {
                Id = "q1", Tenant = "acme", Category = "table-value", Question = "Net sales?",
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Text, Text = "416,161" }, Anchors = [["416,161"]]
            },
            ..extraQuestions.Select((text, i) => new EvalQuestion
            {
                Id = $"x{i}", Tenant = "acme", Category = "table-value", Question = text,
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Text, Text = "416,161" }, Anchors = [["416,161"]]
            })
        ]
    };

    private static EvalRunner Runner(string? judgeDeployment, bool withJudge = true, bool foreignHits = true, string? failWhenBodyContains = null, string documentProfile = "p1") =>
        new(new LumenApiClient(new HttpClient(new StubHandler(foreignHits: foreignHits, failWhenBodyContains: failWhenBodyContains, documentProfile: documentProfile)) { BaseAddress = new Uri("http://stub/") }),
            new Dictionary<string, TenantCredentials> { ["acme"] = new() { Email = "a", Password = "b" } },
            withJudge ? new StubJudge() : null, judgeDeployment);

    private static Task<RunOutcome> Run(EvalRunner runner, RunMode mode, params string[] extraQuestions) =>
        runner.RunAsync(Set(extraQuestions), "inline", new string('0', 64), new RunOptions { Mode = mode, Label = "t" }, CancellationToken.None);

    [Fact]
    public async Task Retrieval_mode_aborts_when_search_returns_a_foreign_document()
    {
        var ex = await Assert.ThrowsAsync<EvalRunAbortedException>(() => Run(Runner(null, withJudge: false), RunMode.Retrieval));

        Assert.IsType<IsolationBreachException>(ex.InnerException);
        Assert.Contains(ForeignDocument.ToString(), ex.InnerException!.Message);
    }

    [Fact]
    public async Task Full_mode_aborts_when_ask_cites_a_foreign_document()
    {
        var ex = await Assert.ThrowsAsync<EvalRunAbortedException>(() => Run(Runner("gpt-judge"), RunMode.Full));

        Assert.IsType<IsolationBreachException>(ex.InnerException);
        Assert.Contains(ForeignDocument.ToString(), ex.InnerException!.Message);
    }

    [Fact]
    public async Task Full_mode_rejects_a_judge_that_is_the_answering_deployment()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Runner("GPT-Answer"), RunMode.Full));

        Assert.Contains("same as the answering deployment", ex.Message);
    }

    [Fact]
    public async Task The_judge_deployment_is_recorded_in_the_header()
    {
        var outcome = await Run(Runner("gpt-judge", foreignHits: false), RunMode.Full);

        Assert.Equal("gpt-judge", outcome.Header.JudgeDeployment);
        Assert.True(Assert.Single(outcome.Results).Correct);
    }

    [Fact]
    public async Task A_mid_run_failure_surfaces_the_results_gathered_so_far()
    {
        var ex = await Assert.ThrowsAsync<EvalRunAbortedException>(
            () => Run(Runner(null, withJudge: false, foreignHits: false, failWhenBodyContains: "Boom"), RunMode.Retrieval, "Boom?"));

        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.Equal("q1", Assert.Single(ex.Partial.Results).Id);
        Assert.Equal("stub failure", ex.Partial.Header.AbortReason);
    }

    [Fact]
    public async Task Refuses_to_run_when_a_document_was_ingested_under_another_profile()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Runner(null, withJudge: false, foreignHits: false, documentProfile: "old"), RunMode.Retrieval));

        Assert.Contains("Own", ex.Message);
        Assert.Contains("old", ex.Message);
        Assert.Contains("p1", ex.Message);
    }

    [Fact]
    public async Task Records_the_corpus_and_the_retrieved_sources()
    {
        var outcome = await Run(Runner(null, withJudge: false, foreignHits: false), RunMode.Retrieval);

        Assert.Equal("p1", outcome.Header.IngestProfile);
        var document = Assert.Single(outcome.Header.Corpus);
        Assert.Equal(("acme", "Own", "p1", 1), (document.Tenant, document.Title, document.IngestProfile, document.ChunkCount));
        var source = Assert.Single(outcome.Results[0].Sources);
        Assert.Equal(1, source.Rank);
        Assert.StartsWith("net sales", source.TextPreview);
    }
}
