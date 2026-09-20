namespace Lumen.Application.Interfaces;

public interface IChunker
{
    List<DocumentChunk> Chunk(Guid documentId, List<DocumentElement> elements);
}
