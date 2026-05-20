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
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ColdHarbour.Api.IntegrationTests.Identity;

// ── shared in-memory stubs ───────────────────────────────────────────────────

internal sealed class InMemoryUserRepository : IUserRepository
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

internal sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
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

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        foreach (var token in _byHash.Values.Where(t => t.FamilyId == familyId))
            token.Revoke();
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteExpiredAndRevokedAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string plaintext) => $"FAKE:{plaintext}";
    public bool Verify(string plaintext, string hash) => hash == $"FAKE:{plaintext}";
}

internal sealed class FakeTokenService(string signingKey, string issuer, string audience) : ITokenService
{
    private JwtSecurityToken BuildToken(IEnumerable<Claim> claims, int expiryMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        return new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    }

    public string GenerateAccessToken(User user, string deviceId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("deviceId", deviceId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        return new JwtSecurityTokenHandler().WriteToken(BuildToken(claims, 15));
    }

    public string GenerateMediaToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        return new JwtSecurityTokenHandler().WriteToken(BuildToken(claims, 480));
    }

    public string GenerateRefreshTokenPlaintext()
        => Convert.ToBase64String(Guid.NewGuid().ToByteArray());
}

internal sealed class FakeLibraryReadRepository : ILibraryReadRepository
{
    public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TrackReadModel>>(Array.Empty<TrackReadModel>());
}

internal sealed class FakeTrackWriteRepo : ColdHarbour.Application.Library.Ports.ITrackRepository
{
    public Task<ColdHarbour.Domain.Library.Track?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Track?>(null);
    public Task<ColdHarbour.Domain.Library.Track?> FindByAudioSha1Async(string sha1, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Track?>(null);
    public Task<ColdHarbour.Domain.Library.Artist?> FindArtistByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Artist?>(null);
    public Task<ColdHarbour.Domain.Library.Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Artist?>(null);
    public Task<ColdHarbour.Domain.Library.Album?> FindAlbumByArtistAndTitleAsync(Guid aid, string title, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Album?>(null);
    public Task<ColdHarbour.Domain.Library.Album?> FindAlbumByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Album?>(null);
    public Task<int> CountTracksByAlbumIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> CountAlbumsByArtistIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
    public Task AddArtistAsync(ColdHarbour.Domain.Library.Artist a, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddAlbumAsync(ColdHarbour.Domain.Library.Album a, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddTrackAsync(ColdHarbour.Domain.Library.Track t, CancellationToken ct = default) => Task.CompletedTask;
    public void RemoveTrack(ColdHarbour.Domain.Library.Track t) { }
    public void RemoveAlbum(ColdHarbour.Domain.Library.Album a) { }
    public void RemoveArtist(ColdHarbour.Domain.Library.Artist a) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<ColdHarbour.Domain.Library.Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default)
        => Task.FromResult(new List<ColdHarbour.Domain.Library.Track>());
}

internal sealed class FakeIngestSvc : ColdHarbour.Application.Library.Ports.ITrackIngestService
{
    public Task<ColdHarbour.Application.Library.Dtos.TrackUploadResultDto> IngestAsync(Stream s, string fn, CancellationToken ct = default)
        => Task.FromResult(new ColdHarbour.Application.Library.Dtos.TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
    public Task RemoveTrackFilesAsync(string? path, string sha1, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeReconciler : ColdHarbour.Application.Library.Ports.ILibraryReconciler
{
    public Task<ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
        => Task.FromResult(new ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto([], [], []));
    public Task ApplyAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeArtwork : ColdHarbour.Application.Library.Ports.IArtworkService
{
    public Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

// ── dedicated factory for auth tests ─────────────────────────────────────────

/// <summary>
/// Custom factory that stubs all identity ports and injects the test JWT signing key.
/// Using a dedicated subclass avoids IClassFixture sharing issues between test classes.
/// </summary>
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    internal const string SigningKey = "coldharbour-test-signing-key-32bytes!!";
    internal const string Issuer = "coldharbour";
    internal const string Audience = "coldharbour-web";

    internal readonly InMemoryUserRepository UserRepo = new();
    internal readonly InMemoryRefreshTokenRepository TokenRepo = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_JWT_SIGNING_KEY"] = SigningKey,
                ["COLDHARBOUR_JWT_ISSUER"] = Issuer,
                ["COLDHARBOUR_JWT_AUDIENCE"] = Audience,
                ["COLDHARBOUR_TEST_MODE"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove EF / real DB
            services.RemoveAll<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>();
            services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<
                ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>));

            // Stub library read repo (used by MusicController)
            services.RemoveAll<ILibraryReadRepository>();
            services.AddScoped<ILibraryReadRepository>(_ => new FakeLibraryReadRepository());

            // Stub library write-side ports so the DI container resolves them
            services.RemoveAll<ColdHarbour.Application.Library.Ports.ITrackRepository>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.ITrackRepository>(_ => new FakeTrackWriteRepo());

            services.RemoveAll<ColdHarbour.Application.Library.Ports.ITrackIngestService>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.ITrackIngestService>(_ => new FakeIngestSvc());

            services.RemoveAll<ColdHarbour.Application.Library.Ports.ILibraryReconciler>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.ILibraryReconciler>(_ => new FakeReconciler());

            services.RemoveAll<ColdHarbour.Application.Library.Ports.IArtworkService>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.IArtworkService>(_ => new FakeArtwork());

            // Stub identity ports
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(UserRepo);

            services.RemoveAll<IRefreshTokenRepository>();
            services.AddSingleton<IRefreshTokenRepository>(TokenRepo);

            services.RemoveAll<IPasswordHasher>();
            services.AddSingleton<IPasswordHasher>(new FakePasswordHasher());

            services.RemoveAll<ITokenService>();
            services.AddSingleton<ITokenService>(new FakeTokenService(SigningKey, Issuer, Audience));

            services.RemoveAll<ColdHarbour.Application.Playback.Ports.IDeviceRepository>();
            services.AddScoped<ColdHarbour.Application.Playback.Ports.IDeviceRepository>(_ => new NullDeviceRepo());

            services.RemoveAll<ColdHarbour.Application.Playback.Ports.ITranscodeService>();
            services.AddScoped<ColdHarbour.Application.Playback.Ports.ITranscodeService>(_ => new NullTranscodeService());
            services.RemoveAll<ColdHarbour.Application.Playback.Ports.IPlayEventRepository>();
            services.AddScoped<ColdHarbour.Application.Playback.Ports.IPlayEventRepository>(_ => new NullPlayEventRepo());
            services.RemoveAll<ColdHarbour.Application.Playback.Ports.IPlaybackSessionStore>();
            services.AddSingleton<ColdHarbour.Application.Playback.Ports.IPlaybackSessionStore>(new ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore());
        });
    }
}

// ── auth integration tests ───────────────────────────────────────────────────

/// <summary>
/// Stubs all identity ports so no Postgres connection is needed.
/// A real JWT is generated with a deterministic test key so JwtBearer validation passes.
/// </summary>
[Collection("IntegrationTests")]
public class AuthIntegrationTests : IClassFixture<AuthTestFactory>
{
    private const string TestSigningKey = AuthTestFactory.SigningKey;
    private const string TestIssuer = AuthTestFactory.Issuer;
    private const string TestAudience = AuthTestFactory.Audience;
    private const string TestEmail = "test@example.com";
    private const string TestPassword = "password123";
    private const string TestDeviceId = "device-test-001";

    private readonly HttpClient _client;
    private readonly InMemoryUserRepository _userRepo;
    private readonly InMemoryRefreshTokenRepository _tokenRepo;

    public AuthIntegrationTests(AuthTestFactory factory)
    {
        _userRepo = factory.UserRepo;
        _tokenRepo = factory.TokenRepo;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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
        await RegisterAsync();                                     // first user
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
        // Cookie attribute names are lowercased by ASP.NET Core's HttpClient handler
        cookies!.Should().Contain(c =>
            c.StartsWith("refreshToken=", StringComparison.OrdinalIgnoreCase) &&
            c.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
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
        // max-age=0 clears the cookie; attribute names are lowercased
        respCookies!.Should().Contain(c =>
            c.StartsWith("refreshToken=", StringComparison.OrdinalIgnoreCase) &&
            c.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
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
}

internal sealed class NullDeviceRepo : ColdHarbour.Application.Playback.Ports.IDeviceRepository
{
    public Task<ColdHarbour.Domain.Playback.Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.Device?>(null);
    public Task<IReadOnlyList<ColdHarbour.Domain.Playback.Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ColdHarbour.Domain.Playback.Device>>([]);
    public Task AddAsync(ColdHarbour.Domain.Playback.Device device, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NullTranscodeService : ColdHarbour.Application.Playback.Ports.ITranscodeService
{
    public Task<string?> GetOrTranscodeAsync(string sourcePath, string audioSha1, string profile, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

internal sealed class NullPlayEventRepo : ColdHarbour.Application.Playback.Ports.IPlayEventRepository
{
    public Task AddAsync(ColdHarbour.Domain.Playback.PlayEvent e, CancellationToken ct = default) => Task.CompletedTask;
    public Task<ColdHarbour.Domain.Playback.PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.PlayEvent?>(null);
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
