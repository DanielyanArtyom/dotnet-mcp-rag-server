namespace Lumen.Api.DTO.Responses;

public class SearchResultDto
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = default!;
    public int ChunkIndex { get; set; }
    public string ChunkType { get; set; } = default!;
    public int TokenCount { get; set; }
    public string Text { get; set; } = default!;
    public double Distance { get; set; }
    public double? Score { get; set; }
}
