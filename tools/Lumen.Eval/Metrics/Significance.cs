namespace Lumen.Eval.Metrics;

public static class Significance
{
    public static double McNemarExact(int aOnly, int bOnly)
    {
        var n = aOnly + bOnly;
        if (n == 0)
        {
            return 1.0;
        }

        var tail = 0.0;
        for (var i = 0; i <= Math.Min(aOnly, bOnly); i++)
        {
            tail += Binomial(n, i);
        }

        return Math.Min(1.0, 2 * tail * Math.Pow(0.5, n));
    }

    public static double PairedRandomization(IReadOnlyList<double> a, IReadOnlyList<double> b, int iterations = 10_000, int seed = 42)
    {
        if (a.Count != b.Count)
        {
            throw new ArgumentException("Paired samples must have the same length.");
        }

        var diffs = a.Zip(b, (x, y) => y - x).ToArray();
        if (diffs.Length == 0 || diffs.All(d => d == 0))
        {
            return 1.0;
        }

        var observed = Math.Abs(diffs.Average());
        var random = new Random(seed);
        var extreme = 0;

        for (var i = 0; i < iterations; i++)
        {
            var sum = 0.0;
            foreach (var d in diffs)
            {
                sum += random.Next(2) == 0 ? d : -d;
            }

            if (Math.Abs(sum / diffs.Length) >= observed - 1e-12)
            {
                extreme++;
            }
        }

        return (extreme + 1.0) / (iterations + 1.0);
    }

    private static double Binomial(int n, int k)
    {
        var result = 1.0;
        for (var i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }
}
