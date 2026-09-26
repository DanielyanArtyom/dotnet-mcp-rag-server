using Lumen.Infrastructure.Constants;

namespace Lumen.UnitTests.Embeddings;

public class FakeEmbeddingServiceTests
{
    private readonly FakeEmbeddingService _service = new();

    [Fact]
    public async Task Produces_one_vector_of_the_configured_dimension_per_input()
    {
        var vectors = await _service.EmbedAsync(["first", "second", "third"]);

        Assert.Equal(3, vectors.Count);
        Assert.All(vectors, v => Assert.Equal(EmbeddingConstants.Dimensions, v.Length));
    }

    [Fact]
    public async Task Is_deterministic_for_the_same_text()
    {
        var first = await _service.EmbedAsync(["Total net sales increased 2%."]);
        var second = await new FakeEmbeddingService().EmbedAsync(["Total net sales increased 2%."]);

        Assert.Equal(first[0], second[0]);
    }

    [Fact]
    public async Task Produces_different_vectors_for_different_texts()
    {
        var vectors = await _service.EmbedAsync(["revenue", "liabilities"]);

        Assert.NotEqual(vectors[0], vectors[1]);
    }

    [Fact]
    public async Task Produces_unit_length_vectors()
    {
        var vectors = await _service.EmbedAsync(["Segment operating performance"]);

        var norm = Math.Sqrt(vectors[0].Sum(x => (double)x * x));
        Assert.Equal(1d, norm, precision: 5);
    }

    [Fact]
    public async Task Returns_an_empty_result_for_an_empty_input()
    {
        var vectors = await _service.EmbedAsync([]);

        Assert.Empty(vectors);
    }

    [Fact]
    public void Reports_its_model_name()
    {
        Assert.Equal("fake-1536", _service.ModelName);
    }
}
