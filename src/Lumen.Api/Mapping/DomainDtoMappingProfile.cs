namespace Lumen.Api.Mapping;

public class DomainDtoMappingProfile : Profile
{
    public DomainDtoMappingProfile()
    {
        CreateMap<Document, DocumentDto>();
    }
}
