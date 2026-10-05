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

        CreateMap<AnswerSource, AskSourceDto>()
            .ForMember(d => d.ChunkType, o => o.MapFrom(s => s.ChunkType.ToString()));

        CreateMap<AskAnswer, AskResponseDto>()
            .ForMember(d => d.Usage, o => o.MapFrom(s => new AskUsageDto
            {
                InputTokens = s.InputTokens, 
                OutputTokens = s.OutputTokens, 
                ReasoningTokens = s.ReasoningTokens
            }))
            .ForMember(d => d.Timings, o => o.MapFrom(s => new AskTimingsDto
            {
                RetrievalMs = s.RetrievalMs, 
                GenerationMs = s.GenerationMs, 
                TotalMs = s.TotalMs
            }));
    }
}
