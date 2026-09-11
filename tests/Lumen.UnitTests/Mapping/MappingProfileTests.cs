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
}
