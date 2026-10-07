namespace Lumen.UnitTests.Ingestion;

public class EmbeddingInputTests
{
    [Fact]
    public void Prefixes_the_header_when_present()
    {
        Assert.Equal("H\nT", EmbeddingInput.For(new DocumentChunk { ContextHeader = "H", Text = "T" }));
        Assert.Equal("T", EmbeddingInput.For(new DocumentChunk { Text = "T" }));
    }
}
