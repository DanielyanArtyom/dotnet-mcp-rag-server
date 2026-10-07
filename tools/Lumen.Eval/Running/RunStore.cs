namespace Lumen.Eval.Running;

public static class RunStore
{
    public static string Write(string runsRoot, RunOutcome outcome, RunSummary? summary = null)
    {
        var safeLabel = string.Concat(outcome.Header.Label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
        var directory = Path.Combine(runsRoot, $"{outcome.Header.StartedAt:yyyyMMdd-HHmmss}_{safeLabel}");
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "run.json"), JsonSerializer.Serialize(outcome.Header, JsonDefaults.Indented));
        File.WriteAllLines(Path.Combine(directory, "results.jsonl"),
            outcome.Results.Select(r => JsonSerializer.Serialize(r, JsonDefaults.Options)));

        summary ??= RunSummaryBuilder.Build(outcome.Results, outcome.Header.Mode, null);
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, JsonDefaults.Indented));

        return directory;
    }

    public static (RunHeader Header, List<QuestionResult> Results) Load(string runDirectory)
    {
        var header = JsonSerializer.Deserialize<RunHeader>(File.ReadAllText(Path.Combine(runDirectory, "run.json")), JsonDefaults.Options)!;
        var results = File.ReadAllLines(Path.Combine(runDirectory, "results.jsonl"))
            .Where(line => line.Length > 0)
            .Select(line => JsonSerializer.Deserialize<QuestionResult>(line, JsonDefaults.Options)!)
            .ToList();

        return (header, results);
    }
}
