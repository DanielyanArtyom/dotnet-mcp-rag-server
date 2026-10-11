namespace Lumen.Eval.Running;

public class EvalRunner
{
    public const int RetrievalTopK = 10;
    public const int BroadAnchorThreshold = 5;

    private readonly LumenApiClient _api;
    private readonly IReadOnlyDictionary<string, TenantCredentials> _tenants;
    private readonly IAnswerJudge? _judge;
    private readonly string? _judgeDeployment;
    private readonly IAgentClient? _agent;

    public EvalRunner(LumenApiClient api, IReadOnlyDictionary<string, TenantCredentials> tenants, IAnswerJudge? judge, string? judgeDeployment = null, IAgentClient? agent = null)
    {
        _api = api;
        _tenants = tenants;
        _judge = judge;
        _judgeDeployment = judge is null ? null : judgeDeployment;
        _agent = agent;
    }

    public async Task<RunOutcome> RunAsync(EvalSet set, string evalSetPath, string evalSetSha256, RunOptions options, CancellationToken ct)
    {
        if (options.Mode.IsAnswering() && _judge is null && set.Questions.Any(q => q.Expected.Kind == ExpectedKind.Text))
        {
            throw new InvalidOperationException($"A {options.Mode.ToString().ToLowerInvariant()} run needs the Judge settings (Endpoint, Deployment, ApiKey) for text answers.");
        }

        if (options.Mode == RunMode.Agent && _agent is null)
        {
            throw new InvalidOperationException("An agent run needs an agent client.");
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

        if (options.Mode == RunMode.Agent)
        {
            header.AgentCliVersion = await _agent!.GetVersionAsync(ct);
            header.AgentConfiguredModel = _agent.ConfiguredModel;
            header.AgentRulesHash = AgentPromptConstants.RulesHash;
        }

        header.IngestProfile = header.ServerSettings.TryGetProperty("ingestProfile", out var profile) ? profile.GetString() : null;

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
                var chunks = await _api.GetChunksAsync(token, document.Id, ct);
                chunkTexts[tenant].AddRange(chunks.Select(c => c.Text));
                header.Corpus.Add(new CorpusDocument(tenant, document.Title, document.IngestProfile, chunks.Count));
            }
        }

        var stale = header.Corpus.Where(d => header.IngestProfile is null || d.IngestProfile != header.IngestProfile).ToList();
        if (stale.Count > 0)
        {
            throw new InvalidOperationException(
                $"The server ingests with profile '{header.IngestProfile ?? "(none reported)"}', but these documents were ingested differently — re-upload them before running the eval: "
                + string.Join("; ", stale.Select(d => $"{d.Tenant}/{d.Title} = '{d.IngestProfile ?? "(none)"}'")));
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
            .Where(q => options.Mode.IsAnswering() || q.Expected.Kind != ExpectedKind.Refusal)
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

                    switch (options.Mode)
                    {
                        case RunMode.Retrieval:
                            await RunRetrievalAsync(question, result, tokens[question.Tenant], ownDocuments[question.Tenant], ct);
                            break;
                        case RunMode.Full:
                            await RunFullAsync(question, result, tokens[question.Tenant], ownDocuments[question.Tenant], header, ct);
                            break;
                        case RunMode.Agent:
                            await RunAgentAsync(question, result, ownDocuments[question.Tenant], header, ct);
                            break;
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
        result.Sources = ToSources(hits, h => (h.ChunkId, h.DocumentId, h.ChunkIndex, h.ChunkType, h.Distance, h.Score, h.Text));
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
        result.Sources = ToSources(ask.Sources, s => (s.ChunkId, s.DocumentId, s.ChunkIndex, s.ChunkType, s.Distance, s.Score, s.Text));
        result.InputTokens = ask.Usage.InputTokens;
        result.OutputTokens = ask.Usage.OutputTokens;
        result.ReasoningTokens = ask.Usage.ReasoningTokens;
        result.ServerTotalMs = ask.Timings.TotalMs;
        if (result.Labelable)
        {
            result.FirstHitRank = RetrievalMetrics.FirstHitRank(ask.Sources.Select(s => s.Text).ToList(), question.Anchors);
        }

        await ScoreAsync(question, result, ask.Answer, ask.Refused, header, ct);
    }

    private async Task RunAgentAsync(EvalQuestion question, QuestionResult result, HashSet<Guid> own, RunHeader header, CancellationToken ct)
    {
        var credentials = _tenants[question.Tenant];
        var token = await _api.LoginAsync(credentials.Email, credentials.Password, ct);

        var stopwatch = Stopwatch.StartNew();
        var transcript = await _agent!.AskAsync(new AgentRequest(question.Question, token), ct);
        result.LatencyMs = stopwatch.ElapsedMilliseconds;

        EnsureOwnDocuments(transcript.ToolResults.SelectMany(ChunkBlockParser.DocumentIds), own, question.Id);

        if (transcript.PermissionDenials.Count > 0)
        {
            throw new InvalidOperationException(
                $"Question {question.Id}: the claude CLI denied tool calls ({string.Join(", ", transcript.PermissionDenials.Distinct())}) — check --allowedTools; a denied tool must not be scored as a wrong answer.");
        }

        if (transcript.IsError)
        {
            throw new InvalidOperationException($"Question {question.Id}: the claude CLI reported an error: {transcript.Answer}");
        }

        var chunks = transcript.ToolResults.SelectMany(ChunkBlockParser.Parse).ToList();
        header.AgentModel ??= transcript.Model;
        result.ToolCalls = transcript.ToolCalls;
        result.Turns = transcript.Turns;
        result.AgentCostUsd = transcript.CostUsd;
        result.InputTokens = transcript.InputTokens;
        result.OutputTokens = transcript.OutputTokens;
        result.SeenChunkIds = chunks.Select(c => c.ChunkId).Distinct().ToList();
        result.RetrievedTokens = 0;
        if (result.Labelable)
        {
            result.RetrievedRelevant = chunks.Any(c => AnchorMatcher.IsRelevant(c.Text, question.Anchors));
        }

        if (transcript.TimedOut)
        {
            result.Correct = false;
            result.Refused = false;
            result.JudgeReason = "timeout";
            return;
        }

        result.Answer = transcript.Answer;
        // An agent explains its refusal ("these are Apple's filings, not JPMorgan's. <refusal sentence>"), so the
        // sentence is matched anywhere in the answer rather than as the whole answer, as /api/ask's single model does.
        result.Refused = transcript.Answer.Contains(AgentPromptConstants.RefusalSentence, StringComparison.Ordinal);
        await ScoreAsync(question, result, transcript.Answer, result.Refused.Value, header, ct);
    }

    private async Task ScoreAsync(EvalQuestion question, QuestionResult result, string answer, bool refused, RunHeader header, CancellationToken ct)
    {
        switch (question.Expected.Kind)
        {
            case ExpectedKind.Refusal:
                result.Correct = refused;
                break;
            case ExpectedKind.Number:
                result.Correct = !refused && NumberScorer.Matches(
                    answer, question.Expected.Value!.Value, question.Expected.Unit, question.Expected.Tolerance);
                break;
            case ExpectedKind.Text when refused:
                result.Correct = false;
                result.JudgeReason = "refused";
                break;
            case ExpectedKind.Text:
                var verdict = await _judge!.JudgeAsync(question.Question, question.Expected.Text!, answer, ct);
                result.Correct = verdict.Correct;
                result.JudgeReason = verdict.Reason;
                header.JudgeInputTokens += verdict.InputTokens;
                header.JudgeOutputTokens += verdict.OutputTokens;
                break;
        }
    }

    private static List<SourceRecord> ToSources<T>(IReadOnlyList<T> items, Func<T, (Guid ChunkId, Guid DocumentId, int ChunkIndex, string ChunkType, double Distance, double? Score, string Text)> project) =>
        items.Select((item, index) =>
        {
            var s = project(item);
            return new SourceRecord(index + 1, s.ChunkId, s.DocumentId, s.ChunkIndex, s.ChunkType, s.Distance, s.Score,
                s.Text.Length <= 200 ? s.Text : s.Text[..200]);
        }).ToList();

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
