using Lumen.Infrastructure.Persistence.Entities;

namespace Lumen.UnitTests.Mapping;

public class MappingProfileTests
{
    [Theory]
    [InlineData(typeof(DomainEntityMappingProfile))]
    [InlineData(typeof(DomainDtoMappingProfile))]
    public void Profile_is_valid(Type profileType)
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile(profileType), NullLoggerFactory.Instance);
        configuration.AssertConfigurationIsValid();
    }

    [Fact]
    public void Maps_chunk_embeddings_between_domain_and_entity()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<DomainEntityMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var embedding = new float[1536];
        embedding[3] = 1f;

        var entity = mapper.Map<DocumentChunkEntity>(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            Text = "Chunk",
            Embedding = embedding,
            EmbeddingModel = "fake-1536"
        });

        Assert.Equal(1536, entity.Embedding.ToArray().Length);
        Assert.Equal(1f, entity.Embedding.ToArray()[3]);

        var domain = mapper.Map<DocumentChunk>(entity);

        Assert.Equal(embedding, domain.Embedding);
        Assert.Equal("fake-1536", domain.EmbeddingModel);
    }
}
