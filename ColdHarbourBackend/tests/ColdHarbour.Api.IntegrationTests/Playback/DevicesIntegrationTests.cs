using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ColdHarbour.Api.IntegrationTests.Playback;

[Collection("IntegrationTests")]
public sealed class DevicesIntegrationTests : IClassFixture<DevicesTestFactory>
{
    private readonly HttpClient _client;

    public DevicesIntegrationTests(DevicesTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DevicesTestFactory.GenerateToken());
    }

    [Fact]
    public async Task RegisterDevice_ReturnsNoContent()
    {
        var body = JsonSerializer.Serialize(new
        {
            deviceId = Guid.NewGuid(),
            name = "Chrome on macOS",
            supportedCodecs = new[] { "mp3", "flac", "m4a", "wav" },
            preferredProfile = "opus-128",
            bitrateCap = (int?)null
        });

        var response = await _client.PostAsync("/api/devices",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Stream_UnknownTrack_Returns404()
    {
        var response = await _client.GetAsync($"/api/stream/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public sealed class DevicesTestFactory : WebApplicationFactory<Program>
{
    private const string SigningKey = "coldharbour-test-signing-key-32bytes!!";
    private const string Issuer = "coldharbour";
    private const string Audience = "coldharbour-web";

    private readonly SpyDeviceRepository _deviceRepo = new();

    public static string GenerateToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("deviceId", Guid.NewGuid().ToString())
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_JWT_SIGNING_KEY"] = SigningKey,
                ["COLDHARBOUR_JWT_ISSUER"] = Issuer,
                ["COLDHARBOUR_JWT_AUDIENCE"] = Audience,
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>();
            services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<
                ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>));

            // Library stubs
            services.RemoveAll<ILibraryReadRepository>();
            services.AddScoped<ILibraryReadRepository>(_ => new EmptyLibraryReadRepo());
            services.RemoveAll<ColdHarbour.Application.Library.Ports.ITrackRepository>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.ITrackRepository>(_ => new EmptyTrackRepo());
            services.RemoveAll<ColdHarbour.Application.Library.Ports.ITrackIngestService>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.ITrackIngestService>(_ => new NullIngest());
            services.RemoveAll<ColdHarbour.Application.Library.Ports.ILibraryReconciler>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.ILibraryReconciler>(_ => new NullReconciler());
            services.RemoveAll<ColdHarbour.Application.Library.Ports.IArtworkService>();
            services.AddScoped<ColdHarbour.Application.Library.Ports.IArtworkService>(_ => new NullArtwork());

            // Playback stubs
            services.RemoveAll<IDeviceRepository>();
            services.AddScoped<IDeviceRepository>(_ => _deviceRepo);
            services.RemoveAll<ITranscodeService>();
            services.AddScoped<ITranscodeService>(_ => new NullTranscodeService());

            // Identity stubs
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(new AlwaysExistsUserRepo());
            services.RemoveAll<IPasswordHasher>();
            services.AddSingleton<IPasswordHasher>(new PlainHasher());
            services.RemoveAll<ITokenService>();
            services.AddSingleton<ITokenService>(new StubTokenService());
            services.RemoveAll<IRefreshTokenRepository>();
            services.AddSingleton<IRefreshTokenRepository>(new NullRefreshTokenRepo());
        });
    }

    // ── spies ────────────────────────────────────────────────────────────────────

    public sealed class SpyDeviceRepository : IDeviceRepository
    {
        public ColdHarbour.Domain.Playback.Device? Stored { get; private set; }
        public Task<ColdHarbour.Domain.Playback.Device?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.Device?>(null);
        public Task AddAsync(ColdHarbour.Domain.Playback.Device d, CancellationToken ct = default) { Stored = d; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    // ── stubs ────────────────────────────────────────────────────────────────────

    private sealed class EmptyLibraryReadRepo : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TrackReadModel>>([]);
    }

    private sealed class EmptyTrackRepo : ColdHarbour.Application.Library.Ports.ITrackRepository
    {
        public Task<ColdHarbour.Domain.Library.Track?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Track?>(null);
        public Task<ColdHarbour.Domain.Library.Track?> FindByAudioSha1Async(string s, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Track?>(null);
        public Task<ColdHarbour.Domain.Library.Artist?> FindArtistByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Artist?>(null);
        public Task<ColdHarbour.Domain.Library.Artist?> FindArtistByNameAsync(string n, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Artist?>(null);
        public Task<ColdHarbour.Domain.Library.Album?> FindAlbumByArtistAndTitleAsync(Guid a, string t, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Album?>(null);
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
    }

    private sealed class NullIngest : ColdHarbour.Application.Library.Ports.ITrackIngestService
    {
        public Task<ColdHarbour.Application.Library.Dtos.TrackUploadResultDto> IngestAsync(Stream s, string f, CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
        public Task RemoveTrackFilesAsync(string? p, string sha1, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullReconciler : ColdHarbour.Application.Library.Ports.ILibraryReconciler
    {
        public Task<ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto([], [], []));
        public Task ApplyAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullArtwork : ColdHarbour.Application.Library.Ports.IArtworkService
    {
        public Task<string?> GetThumbnailPathAsync(Guid id, int size, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NullTranscodeService : ITranscodeService
    {
        public Task<string?> GetOrTranscodeAsync(string src, string sha1, string profile, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class AlwaysExistsUserRepo : IUserRepository
    {
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> AnyUsersExistAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string p) => p;
        public bool Verify(string p, string h) => p == h;
    }

    private sealed class StubTokenService : ITokenService
    {
        public string GenerateAccessToken(User u, string d) => "stub";
        public string GenerateMediaToken(User u) => "stub-media";
        public string GenerateRefreshTokenPlaintext() => "stub";
    }

    private sealed class NullRefreshTokenRepo : IRefreshTokenRepository
    {
        public Task<RefreshToken?> FindByTokenHashAsync(string h, CancellationToken ct = default) => Task.FromResult<RefreshToken?>(null);
        public Task AddAsync(RefreshToken t, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeFamilyAsync(Guid f, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
