namespace Lumen.UnitTests.Chunking;

public class ChunkingOptionsValidatorTests
{
    private readonly ChunkingOptionsValidator _validator = new();

    [Fact]
    public void Defaults_are_valid() => Assert.True(_validator.Validate(null, new ChunkingOptions()).Succeeded);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(250, 250, 0)]
    [InlineData(250, -1, 0)]
    [InlineData(250, 60, 250)]
    [InlineData(250, 60, -1)]
    public void Rejects_inconsistent_values(int max, int overlap, int merge) =>
        Assert.True(_validator.Validate(null, new ChunkingOptions { MaxTokensPerChunk = max, OverlapTokens = overlap, MergeBelowTokens = merge }).Failed);
}
