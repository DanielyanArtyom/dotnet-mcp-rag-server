using Lumen.Eval.Agents;

namespace Lumen.IntegrationTests.Eval;

[Collection("Postgres collection")]
public class EvalRunnerAgentTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private Dictionary<string, TenantCredentials> _tenants = [];
    private Guid _acmeDocument;

    private sealed class AlwaysCorrectJudge : IAnswerJudge
    {
        public Task<JudgeVerdict> JudgeAsync(string question, string reference, string candidate, CancellationToken ct) =>
            Task.FromResult(new JudgeVerdict(true, "stub", 0, 0));
    }

    private sealed class StubAgent(Func<AgentRequest, AgentTranscript> answer) : IAgentClient
    {
        public List<AgentRequest> Requests { get; } = [];

        public string ConfiguredModel => "sonnet";

        public Task<AgentTranscript> AskAsync(AgentRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }

        public Task<string> GetVersionAsync(CancellationToken ct) => Task.FromResult("stub 1.0");
    }

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var a = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Agent eval A", Role.Admin);
        var b = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Agent eval B", Role.Admin);
        _tenants = new()
        {
            ["acme"] = new TenantCredentials { Email = a.Email, Password = a.Password },
            ["globex"] = new TenantCredentials { Email = b.Email, Password = b.Password }
        };
        _acmeDocument = await UploadAsync(a.Email, a.Password, "<p>Total net sales were 416,161.</p>");
        await UploadAsync(b.Email, b.Password, "<p>The CET1 ratio was 15.7%.</p>");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> UploadAsync(string email, string password, string body)
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent("Filing"), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!.Id;
    }

    private static string Block(Guid documentId, string text) =>
        $"## [1] Filing (filed 2025-09-27)\ndocumentId: {documentId}  chunkId: {Guid.NewGuid()}  type: prose\n\n<<<chunk\n{text}\nchunk>>>";

    private static EvalSet Set() => new()
    {
        Version = 1,
        Questions =
        [
            new EvalQuestion
            {
                Id = "acme-sales", Tenant = "acme", Category = "table-value", Question = "What were total net sales?",
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Number, Value = 416161 }, Anchors = [["416,161"]]
            },
            new EvalQuestion
            {
                Id = "globex-trap", Tenant = "globex", Category = "cross-tenant", Question = "What were Apple's net sales?",
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Refusal }
            }
        ]
    };

    private EvalRunner Runner(IAgentClient agent) =>
        new(new LumenApiClient(_factory.CreateClient()), _tenants, new AlwaysCorrectJudge(), "judge", agent);

    [Fact]
    public async Task Agent_run_scores_answers_detects_refusals_and_records_retrieval_and_tools()
    {
        var agent = new StubAgent(r => r.Question.Contains("Apple")
            ? new AgentTranscript { Answer = "The provided documents do not contain the answer to this question.", ToolCalls = ["mcp__lumen__search_filings"], ToolResults = ["No matching passages."], Model = "claude-x", CostUsd = 0.01m }
            : new AgentTranscript { Answer = "Total net sales were $416,161 [1].", ToolCalls = ["mcp__lumen__search_filings"], ToolResults = [Block(_acmeDocument, "Total net sales were 416,161.")], Model = "claude-x", CostUsd = 0.02m });

        var outcome = await Runner(agent).RunAsync(Set(), "eval-set.json", new string('a', 64),
            new RunOptions { Mode = RunMode.Agent, Label = "agent-test" }, CancellationToken.None);

        var sales = outcome.Results.Single(r => r.Id == "acme-sales");
        Assert.True(sales.Correct);
        Assert.True(sales.RetrievedRelevant);
        Assert.Equal(["mcp__lumen__search_filings"], sales.ToolCalls);
        var trap = outcome.Results.Single(r => r.Id == "globex-trap");
        Assert.True(trap.Refused);
        Assert.True(trap.Correct);
        Assert.Equal("claude-x", outcome.Header.AgentModel);
        Assert.Equal("sonnet", outcome.Header.AgentConfiguredModel);
        Assert.Equal("stub 1.0", outcome.Header.AgentCliVersion);
        Assert.NotNull(outcome.Header.AgentRulesHash);
        Assert.Equal(2, agent.Requests.Select(r => r.Token).Distinct().Count());
    }

    [Fact]
    public async Task A_refusal_with_an_explanation_still_counts_as_a_refusal()
    {
        var agent = new StubAgent(r => r.Question.Contains("Apple")
            ? new AgentTranscript { Answer = "These are JPMorgan's filings, not Apple's.\n\nThe provided documents do not contain the answer to this question.", ToolResults = [] }
            : new AgentTranscript { Answer = "Total net sales were $416,161 [1].", ToolResults = [] });

        var outcome = await Runner(agent).RunAsync(Set(), "eval-set.json", new string('a', 64),
            new RunOptions { Mode = RunMode.Agent, Label = "agent-refusal" }, CancellationToken.None);

        var trap = outcome.Results.Single(r => r.Id == "globex-trap");
        Assert.True(trap.Refused);
        Assert.True(trap.Correct);
        Assert.False(outcome.Results.Single(r => r.Id == "acme-sales").Refused);
    }

    [Fact]
    public async Task A_foreign_document_id_in_any_tool_output_aborts_the_run_as_an_isolation_breach()
    {
        var agent = new StubAgent(r => r.Question.Contains("Apple")
            ? new AgentTranscript { Answer = "x", ToolResults = [Block(_acmeDocument, "Total net sales were 416,161.")] }
            : new AgentTranscript { Answer = "x", ToolResults = [] });

        var ex = await Assert.ThrowsAsync<EvalRunAbortedException>(() => Runner(agent).RunAsync(Set(), "eval-set.json", new string('a', 64),
            new RunOptions { Mode = RunMode.Agent, Label = "agent-breach" }, CancellationToken.None));

        Assert.IsType<IsolationBreachException>(ex.InnerException);
    }

    [Fact]
    public async Task A_timed_out_question_is_recorded_as_wrong_and_the_run_continues()
    {
        var agent = new StubAgent(r => r.Question.Contains("Apple")
            ? new AgentTranscript { TimedOut = true }
            : new AgentTranscript { Answer = "Total net sales were $416,161 [1].", ToolResults = [] });

        var outcome = await Runner(agent).RunAsync(Set(), "eval-set.json", new string('a', 64),
            new RunOptions { Mode = RunMode.Agent, Label = "agent-timeout" }, CancellationToken.None);

        var trap = outcome.Results.Single(r => r.Id == "globex-trap");
        Assert.False(trap.Correct);
        Assert.Equal("timeout", trap.JudgeReason);
        Assert.Equal(2, outcome.Results.Count);
    }

    [Fact]
    public async Task A_cli_reported_error_aborts_the_run_instead_of_being_scored()
    {
        var agent = new StubAgent(r => r.Question.Contains("Apple")
            ? new AgentTranscript { Answer = "API Error: 529 overloaded", IsError = true }
            : new AgentTranscript { Answer = "Total net sales were $416,161 [1].", ToolResults = [] });

        var ex = await Assert.ThrowsAsync<EvalRunAbortedException>(() => Runner(agent).RunAsync(Set(), "eval-set.json", new string('a', 64),
            new RunOptions { Mode = RunMode.Agent, Label = "agent-error" }, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("API Error: 529 overloaded", ex.InnerException!.Message);
        Assert.Single(ex.Partial.Results);
    }

    [Fact]
    public async Task A_foreign_document_id_seen_before_a_timeout_still_aborts_the_run_as_an_isolation_breach()
    {
        var agent = new StubAgent(r => r.Question.Contains("Apple")
            ? new AgentTranscript { TimedOut = true, ToolResults = [Block(_acmeDocument, "Total net sales were 416,161.")] }
            : new AgentTranscript { Answer = "Total net sales were $416,161 [1].", ToolResults = [] });

        var ex = await Assert.ThrowsAsync<EvalRunAbortedException>(() => Runner(agent).RunAsync(Set(), "eval-set.json", new string('a', 64),
            new RunOptions { Mode = RunMode.Agent, Label = "agent-timeout-breach" }, CancellationToken.None));

        Assert.IsType<IsolationBreachException>(ex.InnerException);
    }

    [Fact]
    public async Task A_permission_denied_tool_call_aborts_the_run_instead_of_being_scored()
    {
        var agent = new StubAgent(r => r.Question.Contains("Apple")
            ? new AgentTranscript { Answer = "The provided documents do not contain the answer to this question.", PermissionDenials = ["mcp__lumen__search_filings"] }
            : new AgentTranscript { Answer = "Total net sales were $416,161 [1].", ToolResults = [] });

        var ex = await Assert.ThrowsAsync<EvalRunAbortedException>(() => Runner(agent).RunAsync(Set(), "eval-set.json", new string('a', 64),
            new RunOptions { Mode = RunMode.Agent, Label = "agent-denied" }, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("mcp__lumen__search_filings", ex.InnerException!.Message);
        Assert.Single(ex.Partial.Results);
    }
}
