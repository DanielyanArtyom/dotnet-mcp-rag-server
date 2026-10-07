namespace Lumen.UnitTests.Embeddings;

public class AzureOpenAIEmbeddingServiceTests
{
    private sealed class StubEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<List<string>> Calls { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var batch = values.ToList();
            Calls.Add(batch);

            var embeddings = new GeneratedEmbeddings<Embedding<float>>(
                batch.Select(v => new Embedding<float>(new float[] { v.Length, 0f, 0f })));

            return Task.FromResult(embeddings);
        }

        public object? GetService(Type serviceType, object? serviceKey) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task Splits_the_input_into_batches_of_the_configured_size()
    {
        var generator = new StubEmbeddingGenerator();
        var service = new AzureOpenAIEmbeddingService(generator, "text-embedding-3-small", maxBatchSize: 2);

        await service.EmbedAsync(["a", "bb", "ccc", "dddd", "eeeee"]);

        Assert.Equal(3, generator.Calls.Count);
        Assert.Equal(["a", "bb"], generator.Calls[0]);
        Assert.Equal(["ccc", "dddd"], generator.Calls[1]);
        Assert.Equal(["eeeee"], generator.Calls[2]);
    }

    [Fact]
    public async Task Returns_one_vector_per_input_in_input_order()
    {
        var generator = new StubEmbeddingGenerator();
        var service = new AzureOpenAIEmbeddingService(generator, "text-embedding-3-small", maxBatchSize: 2);

        var vectors = await service.EmbedAsync(["a", "bb", "ccc"]);

        Assert.Equal(3, vectors.Count);
        Assert.Equal(1f, vectors[0][0]);
        Assert.Equal(2f, vectors[1][0]);
        Assert.Equal(3f, vectors[2][0]);
    }

    [Fact]
    public async Task Does_not_call_the_generator_for_an_empty_input()
    {
        var generator = new StubEmbeddingGenerator();
        var service = new AzureOpenAIEmbeddingService(generator, "text-embedding-3-small", maxBatchSize: 512);

        var vectors = await service.EmbedAsync([]);

        Assert.Empty(vectors);
        Assert.Empty(generator.Calls);
    }

    [Fact]
    public void Reports_the_configured_model_name()
    {
        var service = new AzureOpenAIEmbeddingService(new StubEmbeddingGenerator(), "text-embedding-3-large", maxBatchSize: 512);

        Assert.Equal("text-embedding-3-large", service.ModelName);
    }
}
