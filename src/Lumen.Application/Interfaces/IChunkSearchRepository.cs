namespace Lumen.Application.Interfaces;

public interface IChunkSearchRepository
{
    Task<List<ChunkSearchResult>> SearchAsync(
        float[] queryEmbedding,
        string embeddingModel,
        int topK,
        Guid? documentId,
        CancellationToken ct = default);
}
