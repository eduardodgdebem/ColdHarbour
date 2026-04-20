using System.IdentityModel.Tokens.Jwt;
using ColdHarbour.Domain.Identity;
using ColdHarbour.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace ColdHarbour.Infrastructure.Tests.Identity;

public class TokenServiceTests
{
    private readonly TokenService _sut;

    public TokenServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_JWT_SIGNING_KEY"] = "test-signing-key-that-is-at-least-32-bytes-long!",
                ["COLDHARBOUR_JWT_ISSUER"] = "test-issuer",
                ["COLDHARBOUR_JWT_AUDIENCE"] = "test-audience",
                ["COLDHARBOUR_ACCESS_TOKEN_TTL"] = "15m"
            })
            .Build();

        _sut = new TokenService(config);
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidJwt()
    {
        var user = User.Create(
            "test@example.com",
            "Test User",
            PasswordHash.From("$argon2id$v=19$m=65536,t=3,p=4$fakehash"));
        var deviceId = Guid.NewGuid().ToString();

        var token = _sut.GenerateAccessToken(user, deviceId);

        token.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(token).Should().BeTrue();

        var parsed = handler.ReadJwtToken(token);

        parsed.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        parsed.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "test@example.com");
        parsed.Claims.Should().Contain(c => c.Type == "role");
        parsed.Claims.Should().Contain(c => c.Type == "deviceId" && c.Value == deviceId);
        parsed.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public void GenerateRefreshTokenPlaintext_Returns64CharHexString()
    {
        var token = _sut.GenerateRefreshTokenPlaintext();

        token.Should().NotBeNullOrWhiteSpace();
        token.Should().HaveLength(64);
        token.Should().MatchRegex("^[0-9a-f]{64}$", "refresh token must be 256-bit lowercase hex");
    }

    [Fact]
    public void GenerateRefreshTokenPlaintext_ReturnsUniqueValuesEachCall()
    {
        var t1 = _sut.GenerateRefreshTokenPlaintext();
        var t2 = _sut.GenerateRefreshTokenPlaintext();

        t1.Should().NotBe(t2);
    }
}
