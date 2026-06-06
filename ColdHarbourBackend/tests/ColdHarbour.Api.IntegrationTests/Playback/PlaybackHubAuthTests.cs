using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using ColdHarbour.Api.Playback;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Phase 2 of WS_PROTOCOL_HARDENING: the hub must distinguish an *expired* access token
/// (recoverable — the client refreshes and reconnects) from an *invalid* one (not
/// recoverable). Expiry closes with 4001/token_expired; invalid closes with
/// 1008/invalid_token. Previously both returned null and closed 1008, leaving the
/// frontend's 4001 refresh branch dead and producing a reconnect loop on a stale token.
///
/// Following the repo's WS-testing pattern, the auth decision and close-code mapping are
/// pure/near-pure and tested directly rather than through TestServer's WS lifecycle.
/// </summary>
public sealed class PlaybackHubAuthTests
{
    private const string SigningKey = "phase2-test-signing-key-at-least-32-bytes-long!!";
    private const string Issuer = "coldharbour";
    private const string Audience = "coldharbour-web";

    private static PlaybackSessionHub Hub()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_JWT_SIGNING_KEY"] = SigningKey,
                ["COLDHARBOUR_JWT_ISSUER"] = Issuer,
                ["COLDHARBOUR_JWT_AUDIENCE"] = Audience,
            })
            .Build();

        return new PlaybackSessionHub(null!, null!, null!, null!, config, NullLogger<PlaybackSessionHub>.Instance);
    }

    private static HttpContext ContextWithToken(string token)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?access_token=" + token);
        return ctx;
    }

    private static string MakeToken(string signingKey, DateTime expires, Guid? userId = null, Guid? deviceId = null)
    {
        var claims = new List<Claim> { new("sub", (userId ?? Guid.NewGuid()).ToString()) };
        if (deviceId is { } d) claims.Add(new Claim("deviceId", d.ToString()));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience, claims: claims,
            notBefore: expires.AddMinutes(-30), expires: expires, signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public void Authenticate_returns_Ok_with_claims_for_a_valid_token()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var token = MakeToken(SigningKey, DateTime.UtcNow.AddMinutes(15), userId, deviceId);

        var result = Hub().Authenticate(ContextWithToken(token));

        result.Status.Should().Be(AuthStatus.Ok);
        result.UserId.Should().Be(userId);
        result.DeviceId.Should().Be(deviceId);
    }

    [Fact]
    public void Authenticate_returns_Expired_for_an_expired_token()
    {
        // 10 minutes in the past beats the JwtSecurityTokenHandler default 5-min clock skew.
        var token = MakeToken(SigningKey, DateTime.UtcNow.AddMinutes(-10));

        var result = Hub().Authenticate(ContextWithToken(token));

        result.Status.Should().Be(AuthStatus.Expired);
    }

    [Fact]
    public void Authenticate_returns_Invalid_for_a_wrong_signature()
    {
        var token = MakeToken("a-completely-different-signing-key-32-bytes-xx!!", DateTime.UtcNow.AddMinutes(15));

        var result = Hub().Authenticate(ContextWithToken(token));

        result.Status.Should().Be(AuthStatus.Invalid);
    }

    [Fact]
    public void Authenticate_returns_Invalid_when_no_token_is_present()
    {
        var result = Hub().Authenticate(new DefaultHttpContext());

        result.Status.Should().Be(AuthStatus.Invalid);
    }

    [Fact]
    public void CloseInfoFor_maps_Expired_to_4001_token_expired()
    {
        var (status, description) = PlaybackSessionHub.CloseInfoFor(AuthStatus.Expired);

        ((int)status).Should().Be(4001);
        description.Should().Be("token_expired");
    }

    [Fact]
    public void CloseInfoFor_maps_Invalid_to_1008_invalid_token()
    {
        var (status, description) = PlaybackSessionHub.CloseInfoFor(AuthStatus.Invalid);

        status.Should().Be(WebSocketCloseStatus.PolicyViolation);
        description.Should().Be("invalid_token");
    }
}
