namespace Lumen.Infrastructure.Mapping;

public class DomainEntityMappingProfile : Profile
{
    public DomainEntityMappingProfile()
    {
        CreateMap<Document, DocumentEntity>().ReverseMap();
        CreateMap<User, UserEntity>().ReverseMap();
        CreateMap<Tenant, TenantEntity>().ReverseMap();
    }
}
