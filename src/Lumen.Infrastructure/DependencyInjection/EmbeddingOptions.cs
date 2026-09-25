namespace Lumen.Infrastructure.DependencyInjection;

public class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    public EmbeddingProvider Provider { get; set; } = EmbeddingProvider.Fake;
    public string Model { get; set; } = "text-embedding-3-small";
    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int MaxBatchSize { get; set; } = 512;
}
