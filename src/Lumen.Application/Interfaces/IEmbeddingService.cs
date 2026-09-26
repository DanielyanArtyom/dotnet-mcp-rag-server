namespace Lumen.Application.Interfaces;

public interface IEmbeddingService
{
    string ModelName { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
