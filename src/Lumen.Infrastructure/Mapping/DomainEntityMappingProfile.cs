namespace Lumen.Infrastructure.Mapping;

public class DomainEntityMappingProfile : Profile
{
    public DomainEntityMappingProfile()
    {
        CreateMap<Document, DocumentEntity>().ReverseMap();

        CreateMap<DocumentChunk, DocumentChunkEntity>()
            .ForMember(e => e.Embedding, o => o.MapFrom(c => new Vector(c.Embedding)));

        CreateMap<DocumentChunkEntity, DocumentChunk>()
            .ForMember(c => c.Embedding, o => o.MapFrom(e => e.Embedding.ToArray()));

        CreateMap<User, UserEntity>().ReverseMap();
        CreateMap<Tenant, TenantEntity>().ReverseMap();
    }
}
