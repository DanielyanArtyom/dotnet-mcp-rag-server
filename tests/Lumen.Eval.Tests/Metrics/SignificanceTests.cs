namespace Lumen.Eval.Tests.Metrics;

public class SignificanceTests
{
    [Theory]
    [InlineData(6, 0, 0.03125)]
    [InlineData(8, 1, 0.0390625)]
    [InlineData(5, 0, 0.0625)]
    [InlineData(2, 0, 0.5)]
    [InlineData(3, 3, 1.0)]
    [InlineData(0, 0, 1.0)]
    public void Exact_mcnemar_p_values(int aOnly, int bOnly, double expected) =>
        Assert.Equal(expected, Significance.McNemarExact(aOnly, bOnly), precision: 10);

    [Fact]
    public void Randomization_test_finds_no_difference_between_identical_runs()
    {
        double[] a = [1, 0.5, 0, 1, 0.33];

        Assert.Equal(1.0, Significance.PairedRandomization(a, a));
    }

    [Fact]
    public void Randomization_test_detects_a_consistent_improvement()
    {
        var a = Enumerable.Repeat(0.2, 25).ToArray();
        var b = Enumerable.Repeat(1.0, 25).ToArray();

        Assert.True(Significance.PairedRandomization(a, b) < 0.001);
    }

    [Fact]
    public void Randomization_test_is_deterministic_for_a_seed()
    {
        double[] a = [1, 0, 0.5, 0, 1, 0.25];
        double[] b = [1, 1, 0.5, 0.5, 0, 1];

        Assert.Equal(Significance.PairedRandomization(a, b), Significance.PairedRandomization(a, b));
    }
}
