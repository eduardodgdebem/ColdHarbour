using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ColdHarbour.Infrastructure.Identity;

public sealed class TokenService : ITokenService
{
    private readonly string _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _accessTokenTtl;

    public TokenService(IConfiguration config)
    {
        _signingKey = config["COLDHARBOUR_JWT_SIGNING_KEY"]
            ?? throw new InvalidOperationException("COLDHARBOUR_JWT_SIGNING_KEY is not configured.");

        if (Encoding.UTF8.GetByteCount(_signingKey) < 32)
            throw new InvalidOperationException("COLDHARBOUR_JWT_SIGNING_KEY must be at least 32 bytes (UTF-8).");

        _issuer = config["COLDHARBOUR_JWT_ISSUER"] ?? "coldharbour";
        _audience = config["COLDHARBOUR_JWT_AUDIENCE"] ?? "coldharbour-web";
        _accessTokenTtl = ParseTtl(config["COLDHARBOUR_ACCESS_TOKEN_TTL"] ?? "15m");
    }

    public string GenerateAccessToken(User user, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTimeOffset.UtcNow;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", user.Role.ToString()),
            new Claim("deviceId", deviceId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(_accessTokenTtl).UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshTokenPlaintext()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    // ── helpers ─────────────────────────────────────────────────────────────

    private static TimeSpan ParseTtl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.FromMinutes(15);

        value = value.Trim();

        if (value.EndsWith('m') && int.TryParse(value[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);

        if (value.EndsWith('h') && int.TryParse(value[..^1], out var hours))
            return TimeSpan.FromHours(hours);

        if (value.EndsWith('d') && int.TryParse(value[..^1], out var days))
            return TimeSpan.FromDays(days);

        if (TimeSpan.TryParse(value, out var ts))
            return ts;

        return TimeSpan.FromMinutes(15);
    }
}
