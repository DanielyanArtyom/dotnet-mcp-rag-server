using Lumen.Domain.Models;

namespace Lumen.Api.Mapping;

public class DomainDtoMappingProfile : Profile
{
    public DomainDtoMappingProfile()
    {
        CreateMap<Document, DocumentDto>()
            .ForMember(d => d.ChunkCount, o => o.Ignore());

        CreateMap<DocumentChunk, DocumentChunkDto>()
            .ForMember(d => d.ChunkType, o => o.MapFrom(s => s.ChunkType.ToString()));

        CreateMap<ChunkSearchResult, SearchResultDto>()
            .ForMember(d => d.ChunkType, o => o.MapFrom(s => s.ChunkType.ToString()));
    }
}
