using System.Globalization;

namespace Lumen.Eval.Scoring;

public static class NumberScorer
{
    public const decimal DefaultTolerance = 0.005m;

    public static readonly IReadOnlySet<string> KnownUnits =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "thousands", "millions", "billions", "percent" };

    private static readonly Regex Citation = new(@"\[[0-9]+(?:\s*,\s*[0-9]+)*\]", RegexOptions.Compiled);

    private static readonly Regex Number = new(
        @"\$?\s*(?<open>\()?\s*(?<neg>(?<!\w)[-−](?=\$?[0-9]))?\$?\s*(?<num>[0-9]{1,3}(?:,[0-9]{3})+(?:\.[0-9]+)?|[0-9]+(?:\.[0-9]+)?)\s*(?<close>\))?\s*(?<scale>thousand|million|billion|trillion|%|percent)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool Matches(string answer, decimal expected, string? unit, decimal? tolerance)
    {
        var target = expected * UnitScale(unit);
        var allowed = tolerance ?? DefaultTolerance;

        return ExtractCandidates(answer, unit).Any(candidate => target == 0
            ? Math.Abs(candidate) <= allowed
            : Math.Abs(candidate - target) <= allowed * Math.Abs(target));
    }

    public static IReadOnlyList<decimal> ExtractCandidates(string answer, string? unit)
    {
        var unitScale = UnitScale(unit);
        var candidates = new List<decimal>();

        foreach (Match match in Number.Matches(Citation.Replace(answer, " ")))
        {
            var value = decimal.Parse(match.Groups["num"].Value.Replace(",", string.Empty), CultureInfo.InvariantCulture);
            if (match.Groups["neg"].Success || (match.Groups["open"].Success && match.Groups["close"].Success))
            {
                value = -value;
            }

            candidates.Add(match.Groups["scale"].Value.ToLowerInvariant() switch
            {
                "thousand" => value * 1_000m,
                "million" => value * 1_000_000m,
                "billion" => value * 1_000_000_000m,
                "trillion" => value * 1_000_000_000_000m,
                "%" or "percent" => value,
                _ => value * unitScale
            });
        }

        return candidates;
    }

    private static decimal UnitScale(string? unit) => (unit ?? string.Empty).ToLowerInvariant() switch
    {
        "" or "percent" => 1m,
        "thousands" => 1_000m,
        "millions" => 1_000_000m,
        "billions" => 1_000_000_000m,
        _ => throw new ArgumentException($"Unknown unit '{unit}'.", nameof(unit))
    };
}
