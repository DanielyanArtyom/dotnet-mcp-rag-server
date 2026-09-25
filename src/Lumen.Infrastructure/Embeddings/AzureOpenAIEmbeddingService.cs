namespace Lumen.Infrastructure.Embeddings;

public class AzureOpenAIEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly string _modelName;
    private readonly int _maxBatchSize;

    public AzureOpenAIEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator, string modelName, int maxBatchSize)
    {
        _generator = generator;
        _modelName = modelName;
        _maxBatchSize = maxBatchSize;
    }

    public string ModelName => _modelName;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new List<float[]>(texts.Count);

        for (var start = 0; start < texts.Count; start += _maxBatchSize)
        {
            var batch = texts.Skip(start).Take(_maxBatchSize).ToList();
            var generated = await _generator.GenerateAsync(batch, cancellationToken: ct);

            vectors.AddRange(generated.Select(e => e.Vector.ToArray()));
        }

        return vectors;
    }
}
