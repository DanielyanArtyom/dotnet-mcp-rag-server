using System.ComponentModel.DataAnnotations;

namespace Lumen.Api.DTO.Requests;

public class AskRequest
{
    [Required]
    [MaxLength(2000)]
    public string Question { get; set; } = default!;

    public Guid? DocumentId { get; set; }
}
