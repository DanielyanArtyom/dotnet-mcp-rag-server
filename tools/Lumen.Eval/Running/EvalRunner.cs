using System.Diagnostics;

namespace Lumen.Eval.Running;

public class EvalRunner
{
    public const int RetrievalTopK = 10;
    public const int BroadAnchorThreshold = 5;

    private readonly LumenApiClient _api;
    private readonly IReadOnlyDictionary<string, TenantCredentials> _tenants;
    private readonly IAnswerJudge? _judge;
    private readonly string? _judgeDeployment;

    public EvalRunner(LumenApiClient api, IReadOnlyDictionary<string, TenantCredentials> tenants, IAnswerJudge? judge, string? judgeDeployment = null)
    {
        _api = api;
        _tenants = tenants;
        _judge = judge;
        _judgeDeployment = judge is null ? null : judgeDeployment;
    }

    public async Task<RunOutcome> RunAsync(EvalSet set, string evalSetPath, string evalSetSha256, RunOptions options, CancellationToken ct)
    {
        if (options.Mode == RunMode.Full && _judge is null && set.Questions.Any(q => q.Expected.Kind == ExpectedKind.Text))
        {
            throw new InvalidOperationException("A full run needs the Judge settings (Endpoint, Deployment, ApiKey) for text answers.");
        }

        var tokens = new Dictionary<string, string>();
        foreach (var tenant in set.Questions.Select(q => q.Tenant).Distinct())
        {
            if (!_tenants.TryGetValue(tenant, out var credentials))
            {
                throw new InvalidOperationException($"No credentials configured for tenant '{tenant}'.");
            }

            tokens[tenant] = await _api.LoginAsync(credentials.Email, credentials.Password, ct);
        }

        var header = new RunHeader
        {
            Label = options.Label,
            Mode = options.Mode,
            StartedAt = DateTimeOffset.Now,
            GitCommit = ReadGitCommit(),
            EvalSetPath = evalSetPath,
            EvalSetSha256 = evalSetSha256,
            Repeat = options.Repeat,
            Holdout = options.Holdout,
            ServerSettings = await _api.GetSettingsAsync(tokens.Values.First(), ct),
            JudgeDeployment = _judgeDeployment
        };

        if (options.Mode == RunMode.Full && _judgeDeployment is not null
            && header.ServerSettings.TryGetProperty("chat", out var chat)
            && chat.TryGetProperty("deployment", out var answering)
            && string.Equals(answering.GetString(), _judgeDeployment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The judge deployment '{_judgeDeployment}' is the same as the answering deployment; use a different model to judge.");
        }

        var ownDocuments = new Dictionary<string, HashSet<Guid>>();
        var chunkTexts = new Dictionary<string, List<string>>();
        foreach (var (tenant, token) in tokens)
        {
            var documents = await _api.GetDocumentsAsync(token, ct);
            ownDocuments[tenant] = documents.Select(d => d.Id).ToHashSet();
            chunkTexts[tenant] = [];
            foreach (var document in documents)
            {
                chunkTexts[tenant].AddRange((await _api.GetChunksAsync(token, document.Id, ct)).Select(c => c.Text));
            }
        }

        var labelable = new HashSet<string>();
        foreach (var question in set.Questions.Where(q => q.Expected.Kind != ExpectedKind.Refusal))
        {
            var matches = chunkTexts[question.Tenant].Count(text => AnchorMatcher.IsRelevant(text, question.Anchors));
            if (matches == 0)
            {
                header.Unlabelable.Add(question.Id);
                continue;
            }

            labelable.Add(question.Id);
            if (matches > BroadAnchorThreshold)
            {
                header.BroadAnchors[question.Id] = matches;
            }
        }

        var questions = set.Questions
            .Where(q => q.Holdout == options.Holdout)
            .Where(q => options.Mode == RunMode.Full || q.Expected.Kind != ExpectedKind.Refusal)
            .ToList();

        var outcome = new RunOutcome { Header = header };
        try
        {
            for (var repeat = 0; repeat < options.Repeat; repeat++)
            {
                foreach (var question in questions)
                {
                    var result = new QuestionResult
                    {
                        Id = question.Id,
                        Category = question.Category,
                        Tenant = question.Tenant,
                        ExpectedKind = question.Expected.Kind,
                        Repeat = repeat,
                        Labelable = labelable.Contains(question.Id)
                    };

                    if (options.Mode == RunMode.Retrieval)
                    {
                        await RunRetrievalAsync(question, result, tokens[question.Tenant], ownDocuments[question.Tenant], ct);
                    }
                    else
                    {
                        await RunFullAsync(question, result, tokens[question.Tenant], ownDocuments[question.Tenant], header, ct);
                    }

                    outcome.Results.Add(result);
                }
            }
        }
        catch (Exception ex)
        {
            header.AbortReason = ex.Message;
            throw new EvalRunAbortedException(
                $"Run aborted after {outcome.Results.Count} of {questions.Count * options.Repeat} results: {ex.Message}", ex, outcome);
        }

        return outcome;
    }

    private async Task RunRetrievalAsync(EvalQuestion question, QuestionResult result, string token, HashSet<Guid> own, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var hits = await _api.SearchAsync(token, question.Question, RetrievalTopK, ct);
        result.LatencyMs = stopwatch.ElapsedMilliseconds;

        EnsureOwnDocuments(hits.Select(h => h.DocumentId), own, question.Id);
        result.RetrievedTokens = hits.Sum(h => h.TokenCount);
        if (result.Labelable)
        {
            result.FirstHitRank = RetrievalMetrics.FirstHitRank(hits.Select(h => h.Text).ToList(), question.Anchors);
        }
    }

    private async Task RunFullAsync(EvalQuestion question, QuestionResult result, string token, HashSet<Guid> own, RunHeader header, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var ask = await _api.AskAsync(token, question.Question, ct);
        result.LatencyMs = stopwatch.ElapsedMilliseconds;

        EnsureOwnDocuments(ask.Sources.Select(s => s.DocumentId), own, question.Id);

        result.Answer = ask.Answer;
        result.Refused = ask.Refused;
        result.Truncated = ask.Truncated;
        result.CitedSourceIds = ask.Sources.Where(s => s.Cited).Select(s => s.SourceId).ToList();
        result.RetrievedTokens = ask.Sources.Sum(s => s.TokenCount);
        result.InputTokens = ask.Usage.InputTokens;
        result.OutputTokens = ask.Usage.OutputTokens;
        result.ReasoningTokens = ask.Usage.ReasoningTokens;
        result.ServerTotalMs = ask.Timings.TotalMs;
        if (result.Labelable)
        {
            result.FirstHitRank = RetrievalMetrics.FirstHitRank(ask.Sources.Select(s => s.Text).ToList(), question.Anchors);
        }

        switch (question.Expected.Kind)
        {
            case ExpectedKind.Refusal:
                result.Correct = ask.Refused;
                break;
            case ExpectedKind.Number:
                result.Correct = !ask.Refused && NumberScorer.Matches(
                    ask.Answer, question.Expected.Value!.Value, question.Expected.Unit, question.Expected.Tolerance);
                break;
            case ExpectedKind.Text when ask.Refused:
                result.Correct = false;
                result.JudgeReason = "refused";
                break;
            case ExpectedKind.Text:
                var verdict = await _judge!.JudgeAsync(question.Question, question.Expected.Text!, ask.Answer, ct);
                result.Correct = verdict.Correct;
                result.JudgeReason = verdict.Reason;
                header.JudgeInputTokens += verdict.InputTokens;
                header.JudgeOutputTokens += verdict.OutputTokens;
                break;
        }
    }

    private static void EnsureOwnDocuments(IEnumerable<Guid> documentIds, HashSet<Guid> own, string questionId)
    {
        var foreign = documentIds.Where(id => !own.Contains(id)).Distinct().ToList();
        if (foreign.Count > 0)
        {
            throw new IsolationBreachException(
                $"Question {questionId}: the API returned documents that do not belong to the asking tenant: {string.Join(", ", foreign)}.");
        }
    }

    private static string ReadGitCommit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "describe --always --dirty")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            var output = process!.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
