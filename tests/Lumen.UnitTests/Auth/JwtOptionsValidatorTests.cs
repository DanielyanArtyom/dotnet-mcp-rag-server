namespace Lumen.UnitTests.Auth;

public class JwtOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(string? secret, string issuer = "lumen", string audience = "lumen-clients") =>
        new JwtOptionsValidator().Validate(null, new JwtOptions { Secret = secret!, Issuer = issuer, Audience = audience });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-secret")]
    public void A_missing_or_short_secret_fails_and_names_the_setting(string? secret)
    {
        var result = Validate(secret);

        Assert.True(result.Failed);
        Assert.Contains("Jwt:Secret", result.FailureMessage);
    }

    [Fact]
    public void A_32_byte_secret_with_issuer_and_audience_passes() =>
        Assert.True(Validate(new string('x', 32)).Succeeded);

    [Fact]
    public void Missing_issuer_or_audience_fails()
    {
        Assert.Contains("Jwt:Issuer", Validate(new string('x', 32), issuer: "").FailureMessage);
        Assert.Contains("Jwt:Audience", Validate(new string('x', 32), audience: "").FailureMessage);
    }
}
