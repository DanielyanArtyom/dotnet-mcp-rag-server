namespace Lumen.IntegrationTests.Eval;

[Collection("Postgres collection")]
public class EvalRunnerTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private Dictionary<string, TenantCredentials> _tenants = [];

    private sealed class AlwaysCorrectJudge : IAnswerJudge
    {
        public Task<JudgeVerdict> JudgeAsync(string question, string reference, string candidate, CancellationToken ct) =>
            Task.FromResult(new JudgeVerdict(true, "stub", 0, 0));
    }

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var a = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Eval tenant A", Role.Admin);
        var b = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Eval tenant B", Role.Admin);
        _tenants = new()
        {
            ["acme"] = new TenantCredentials { Email = a.Email, Password = a.Password },
            ["globex"] = new TenantCredentials { Email = b.Email, Password = b.Password }
        };

        await UploadAsync(a.Email, a.Password, "<p>Total net sales were 416,161.</p>");
        await UploadAsync(b.Email, b.Password, "<p>The CET1 ratio was 15.7%.</p>");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task UploadAsync(string email, string password, string body)
    {
        using var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent("Filing"), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();
    }

    private static EvalSet Set() => new()
    {
        Version = 1,
        Questions =
        [
            new EvalQuestion
            {
                Id = "acme-sales", Tenant = "acme", Category = "table-value", Question = "Total net sales were 416,161.",
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Text, Text = "416,161" }, Anchors = [["416,161"]]
            },
            new EvalQuestion
            {
                Id = "globex-cet1", Tenant = "globex", Category = "exact-term", Question = "The CET1 ratio was 15.7%.",
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Text, Text = "15.7%" }, Anchors = [["CET1 ratio"]]
            },
            new EvalQuestion
            {
                Id = "acme-missing", Tenant = "acme", Category = "table-value", Question = "What is the dividend?",
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Text, Text = "n/a" }, Anchors = [["not in any chunk"]]
            },
            new EvalQuestion
            {
                Id = "globex-trap", Tenant = "globex", Category = "cross-tenant", Question = "What were Apple's net sales?",
                Expected = new ExpectedAnswer { Kind = ExpectedKind.Refusal }
            }
        ]
    };

    [Fact]
    public async Task Retrieval_mode_ranks_each_tenants_own_chunk_first_and_marks_unlabelable_questions()
    {
        var runner = new EvalRunner(new LumenApiClient(_factory.CreateClient()), _tenants, judge: null);

        var outcome = await runner.RunAsync(Set(), "inline", new string('0', 64), new RunOptions { Mode = RunMode.Retrieval, Label = "t" }, CancellationToken.None);

        Assert.Contains("acme-missing", outcome.Header.Unlabelable);
        Assert.DoesNotContain(outcome.Results, r => r.Id == "globex-trap");
        Assert.Equal(1, outcome.Results.Single(r => r.Id == "acme-sales").FirstHitRank);
        Assert.Equal(1, outcome.Results.Single(r => r.Id == "globex-cet1").FirstHitRank);
        Assert.Equal("vector", outcome.Header.ServerSettings.GetProperty("searchMode").GetString());
    }

    [Fact]
    public async Task Full_mode_scores_answers_and_never_sees_foreign_documents()
    {
        var runner = new EvalRunner(new LumenApiClient(_factory.CreateClient()), _tenants, new AlwaysCorrectJudge());

        var outcome = await runner.RunAsync(Set(), "inline", new string('0', 64), new RunOptions { Mode = RunMode.Full, Label = "t" }, CancellationToken.None);

        var sales = outcome.Results.Single(r => r.Id == "acme-sales");
        Assert.True(sales.Correct);
        Assert.Contains(1, sales.CitedSourceIds);
        Assert.True(sales.InputTokens > 0);

        var trap = outcome.Results.Single(r => r.Id == "globex-trap");
        Assert.NotNull(trap.Correct);
    }

    [Fact]
    public async Task A_run_against_a_server_with_a_different_profile_is_refused()
    {
        using var otherProfile = _factory.WithWebHostBuilder(b => b.UseSetting("Chunking:ProtectDecimals", "true"));
        var runner = new EvalRunner(new LumenApiClient(otherProfile.CreateClient()), _tenants, judge: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(Set(), "inline", new string('0', 64), new RunOptions { Mode = RunMode.Retrieval, Label = "t" }, CancellationToken.None));
    }
}
