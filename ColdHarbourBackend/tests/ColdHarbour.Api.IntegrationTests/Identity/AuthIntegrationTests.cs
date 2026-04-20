using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Domain.Identity;
using Microsoft.Extensions.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ColdHarbour.Api.IntegrationTests.Identity;

/// <summary>
/// Stubs all identity ports so no Postgres connection is needed.
/// A real JWT is generated with a deterministic test key so JwtBearer validation passes.
/// </summary>
public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    // ── test constants ──────────────────────────────────────────────────────────
    private const string TestSigningKey = "coldharbour-test-signing-key-32bytes!!";
    private const string TestIssuer = "coldharbour";
    private const string TestAudience = "coldharbour-web";
    private const string TestEmail = "test@example.com";
    private const string TestPassword = "password123";
    private const string TestDeviceId = "device-test-001";

    private readonly HttpClient _client;
    private readonly InMemoryUserRepository _userRepo = new();
    private readonly InMemoryRefreshTokenRepository _tokenRepo = new();

    public AuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                // Override JWT config with deterministic test values
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["COLDHARBOUR_JWT_SIGNING_KEY"] = TestSigningKey,
                    ["COLDHARBOUR_JWT_ISSUER"] = TestIssuer,
                    ["COLDHARBOUR_JWT_AUDIENCE"] = TestAudience
                });
            });

            builder.ConfigureServices(services =>
            {
                // Remove EF / real DB
                services.RemoveAll<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>();
                services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<
                    ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>));

                // Stub library repo (used by MusicController)
                services.RemoveAll<ILibraryReadRepository>();
                services.AddScoped<ILibraryReadRepository>(_ => new FakeLibraryReadRepository());

                // Stub identity ports
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(_userRepo);

                services.RemoveAll<IRefreshTokenRepository>();
                services.AddSingleton<IRefreshTokenRepository>(_tokenRepo);

                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher>(new FakePasswordHasher());

                services.RemoveAll<ITokenService>();
                services.AddSingleton<ITokenService>(new FakeTokenService(TestSigningKey, TestIssuer, TestAudience));
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private async Task<string> LoginAndGetAccessTokenAsync()
    {
        var resp = await _client.PostAsync("/api/auth/login",
            Json(new { email = TestEmail, password = TestPassword, deviceId = TestDeviceId }));
        var body = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> RegisterAsync(string email = TestEmail, string name = "Test User", string password = TestPassword)
        => await _client.PostAsync("/api/auth/register", Json(new { email, name, password }));

    private async Task<HttpResponseMessage> LoginAsync(string email = TestEmail, string password = TestPassword, string deviceId = TestDeviceId)
        => await _client.PostAsync("/api/auth/login", Json(new { email, password, deviceId }));

    // ── register tests ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_FirstUser_Returns201()
    {
        _userRepo.Clear();
        var response = await RegisterAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_SecondUser_Returns403()
    {
        _userRepo.Clear();
        await RegisterAsync();                            // first user
        var response = await RegisterAsync("second@example.com"); // second user
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── login tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithAccessToken()
    {
        _userRepo.Clear();
        _tokenRepo.Clear();
        await RegisterAsync();

        var response = await LoginAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401()
    {
        _userRepo.Clear();
        _tokenRepo.Clear();
        await RegisterAsync();

        var response = await LoginAsync(password: "wrongpassword");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_SetsRefreshTokenCookie()
    {
        _userRepo.Clear();
        _tokenRepo.Clear();
        await RegisterAsync();

        var response = await LoginAsync();

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith("refreshToken=") && c.Contains("HttpOnly") && c.Contains("SameSite=Strict"));
    }

    // ── refresh tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidCookie_Returns200WithNewToken()
    {
        _userRepo.Clear();
        _tokenRepo.Clear();
        await RegisterAsync();
        var loginResp = await LoginAsync();

        // Extract cookie and send it back manually
        var cookieHeader = loginResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var cookieValue = cookieHeader.Split(';')[0].Replace("refreshToken=", "").Trim();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={cookieValue}");
        request.Content = Json(new { deviceId = TestDeviceId });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_MissingCookie_Returns401()
    {
        var response = await _client.PostAsync("/api/auth/refresh", Json(new { deviceId = TestDeviceId }));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── logout tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ClearsRefreshTokenCookie()
    {
        _userRepo.Clear();
        _tokenRepo.Clear();
        await RegisterAsync();
        var loginResp = await LoginAsync();

        var cookieHeader = loginResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var cookieValue = cookieHeader.Split(';')[0].Replace("refreshToken=", "").Trim();

        var accessToken = await LoginAndGetAccessTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Cookie", $"refreshToken={cookieValue}");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.TryGetValues("Set-Cookie", out var respCookies).Should().BeTrue();
        respCookies!.Should().Contain(c => c.StartsWith("refreshToken=") && c.Contains("Max-Age=0"));
    }

    // ── playlist auth guard tests ────────────────────────────────────────────────

    [Fact]
    public async Task GetPlaylist_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/music/playlist/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPlaylist_WithValidToken_Returns200()
    {
        _userRepo.Clear();
        _tokenRepo.Clear();
        await RegisterAsync();

        var accessToken = await LoginAndGetAccessTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/music/playlist/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── in-memory stubs ──────────────────────────────────────────────────────────

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<string, User> _byEmail = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Guid, User> _byId = new();

        public void Clear() { _byEmail.Clear(); _byId.Clear(); }

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult(_byEmail.TryGetValue(email, out var u) ? u : null);

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_byId.TryGetValue(id, out var u) ? u : null);

        public Task AddAsync(User user, CancellationToken ct = default)
        {
            _byEmail[user.Email] = user;
            _byId[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task<bool> AnyUsersExistAsync(CancellationToken ct = default)
            => Task.FromResult(!_byEmail.IsEmpty);

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly ConcurrentDictionary<string, RefreshToken> _byHash = new(StringComparer.OrdinalIgnoreCase);

        public void Clear() => _byHash.Clear();

        public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
            => Task.FromResult(_byHash.TryGetValue(tokenHash, out var t) ? t : null);

        public Task AddAsync(RefreshToken token, CancellationToken ct = default)
        {
            _byHash[token.TokenHash] = token;
            return Task.CompletedTask;
        }

        public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
        {
            foreach (var token in _byHash.Values.Where(t => t.FamilyId == familyId))
                token.Revoke();
            await Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string plaintext) => $"FAKE:{plaintext}";
        public bool Verify(string plaintext, string hash) => hash == $"FAKE:{plaintext}";
    }

    private sealed class FakeTokenService(string signingKey, string issuer, string audience) : ITokenService
    {
        public string GenerateAccessToken(User user, string deviceId)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("deviceId", deviceId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshTokenPlaintext()
            => Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    private sealed class FakeLibraryReadRepository : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TrackReadModel>>(Array.Empty<TrackReadModel>());
    }
}
