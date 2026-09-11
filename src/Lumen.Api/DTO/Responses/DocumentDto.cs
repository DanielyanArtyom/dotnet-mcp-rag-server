namespace Lumen.Api.DTO.Responses;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
}
