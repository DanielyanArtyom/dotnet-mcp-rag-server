namespace Lumen.UnitTests.Search;

public class SearchOptionsValidatorTests
{
    private readonly SearchOptionsValidator _validator = new();

    [Fact]
    public void Defaults_are_valid() => Assert.True(_validator.Validate(null, new SearchOptions()).Succeeded);

    [Theory]
    [InlineData(0, 50, 1.0, 1.0, null)]
    [InlineData(60, 0, 1.0, 1.0, null)]
    [InlineData(60, 201, 1.0, 1.0, null)]
    [InlineData(60, 50, -0.1, 1.0, null)]
    [InlineData(60, 50, 0.0, 0.0, null)]
    [InlineData(60, 50, 1.0, 1.0, 0)]
    [InlineData(60, 50, double.NaN, 1.0, null)]
    [InlineData(60, 50, 1.0, double.PositiveInfinity, null)]
    public void Rejects_invalid_values(int rrfK, int candidates, double lexical, double vector, int? maxPerDocument) =>
        Assert.True(_validator.Validate(null, new SearchOptions
        {
            RrfK = rrfK, Candidates = candidates, LexicalWeight = lexical, VectorWeight = vector, MaxChunksPerDocument = maxPerDocument
        }).Failed);
}
