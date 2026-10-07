using System.ClientModel;
using Lumen.Eval;
using Microsoft.Extensions.Configuration;
using OpenAI;

try
{
    return args.FirstOrDefault() switch
    {
        "run" => await RunAsync(args[1..]),
        "compare" when args.Length == 3 => Compare(args[1], args[2]),
        _ => Usage()
    };
}
catch (IsolationBreachException ex)
{
    Console.Error.WriteLine($"TENANT ISOLATION BREACH: {ex.Message}");
    return 2;
}
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FileNotFoundException or HttpRequestException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    var settings = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Local.json", optional: true)
        .AddEnvironmentVariables("LUMEN_EVAL_")
        .Build()
        .Get<EvalSettings>() ?? new EvalSettings();

    var mode = Option(args, "--mode") switch
    {
        "retrieval" => RunMode.Retrieval,
        "full" => RunMode.Full,
        _ => throw new ArgumentException("--mode must be 'retrieval' or 'full'.")
    };
    var label = Option(args, "--label") ?? throw new ArgumentException("--label is required.");
    var repeat = int.TryParse(Option(args, "--repeat"), out var n) && n > 0 ? n : 1;
    var evalSetPath = Option(args, "--eval-set") ?? settings.EvalSetPath;

    var (set, sha) = EvalSetLoader.Load(evalSetPath);

    IAnswerJudge? judge = null;
    if (mode == RunMode.Full && !string.IsNullOrWhiteSpace(settings.Judge.Endpoint))
    {
        var client = new OpenAIClient(new ApiKeyCredential(settings.Judge.ApiKey), new OpenAIClientOptions { Endpoint = new Uri(settings.Judge.Endpoint) });
        judge = new LlmAnswerJudge(client.GetChatClient(settings.Judge.Deployment).AsIChatClient());
    }

    using var http = new HttpClient { BaseAddress = new Uri(settings.ApiBaseUrl), Timeout = TimeSpan.FromMinutes(5) };
    var runner = new EvalRunner(new LumenApiClient(http), settings.Tenants, judge, settings.Judge.Deployment);
    RunOutcome outcome;
    try
    {
        outcome = await runner.RunAsync(set, evalSetPath, sha,
            new RunOptions { Mode = mode, Label = label, Repeat = repeat, Holdout = args.Contains("--holdout") }, CancellationToken.None);
    }
    catch (EvalRunAbortedException ex)
    {
        var partialDirectory = RunStore.Write(settings.RunsPath, ex.Partial);
        Console.Error.WriteLine(ex.InnerException is IsolationBreachException ? $"TENANT ISOLATION BREACH: {ex.Message}" : ex.Message);
        Console.Error.WriteLine($"Partial results ({ex.Partial.Results.Count}) saved to {partialDirectory}");
        return ex.InnerException is IsolationBreachException ? 2 : 1;
    }

    var deployment = outcome.Header.ServerSettings.GetProperty("chat").GetProperty("deployment").GetString() ?? string.Empty;
    var summary = RunSummaryBuilder.Build(outcome.Results, mode, settings.Prices.GetValueOrDefault(deployment));
    var directory = RunStore.Write(settings.RunsPath, outcome, summary);

    ConsoleReport.PrintRun(Console.Out, outcome.Header, outcome.Results, summary, directory);
    return 0;
}

static int Compare(string dirA, string dirB)
{
    var (headerA, resultsA) = RunStore.Load(dirA);
    var (headerB, resultsB) = RunStore.Load(dirB);
    ConsoleReport.PrintComparison(Console.Out, dirA, dirB, RunComparer.Compare(headerA, resultsA, headerB, resultsB));
    return 0;
}

static string? Option(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int Usage()
{
    Console.WriteLine("Usage (from tools/Lumen.Eval):");
    Console.WriteLine("  dotnet run -- run --mode retrieval|full --label <name> [--repeat N] [--holdout] [--eval-set <path>]");
    Console.WriteLine("  dotnet run -- compare <runDirA> <runDirB>");
    return 1;
}
