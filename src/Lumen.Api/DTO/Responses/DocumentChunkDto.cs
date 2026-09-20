namespace Lumen.Api.DTO.Responses;

public class DocumentChunkDto
{
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = default!;
    public int TokenCount { get; set; }
    public string ChunkType { get; set; } = default!;
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
}
