using System.ComponentModel.DataAnnotations;

namespace Lumen.Api.DTO.Requests;

public class SearchRequest
{
    [Required]
    public string Query { get; set; } = default!;

    [Range(1, 50)]
    public int TopK { get; set; } = 5;

    public Guid? DocumentId { get; set; }
}
