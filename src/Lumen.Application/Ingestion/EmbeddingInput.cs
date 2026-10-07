namespace Lumen.Application.Ingestion;

public static class EmbeddingInput
{
    public static string For(DocumentChunk chunk) =>
        chunk.ContextHeader is null ? chunk.Text : chunk.ContextHeader + "\n" + chunk.Text;
}
