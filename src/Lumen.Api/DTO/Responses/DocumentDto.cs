using System.Text.Json.Serialization;

namespace Lumen.Api.DTO.Responses;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public DateOnly FilingDate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ChunkCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
}
