namespace Lumen.UnitTests.Mcp;

public class McpOptionsValidatorTests
{
    [Fact]
    public void Defaults_are_valid() =>
        Assert.True(new McpOptionsValidator().Validate(null, new McpOptions()).Succeeded);

    [Theory]
    [InlineData("")]
    [InlineData("localhost:6274")]
    [InlineData("http://localhost:6274/path")]
    public void Origins_must_be_absolute_scheme_host_port(string origin) =>
        Assert.True(new McpOptionsValidator().Validate(null, new McpOptions { AllowedOrigins = [origin] }).Failed);
}
