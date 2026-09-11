namespace Lumen.UnitTests.Auth;

public class JwtTokenServiceTests
{
    [Fact]
    public void GenerateToken_includes_subject_tenant_and_role_claims()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "unit-test-secret-that-is-long-enough-for-hs256",
            Issuer = "Lumen",
            Audience = "Lumen",
            ExpiryMinutes = 60
        });
        var service = new JwtTokenService(options);
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var token = service.GenerateToken(userId, tenantId, Role.Admin);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(userId.ToString(), jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(tenantId.ToString(), jwt.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.Equal(nameof(Role.Admin), jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
    }
}
