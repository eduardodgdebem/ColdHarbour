using System.Net;
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

namespace ColdHarbour.Api.IntegrationTests.Health;

[Collection("IntegrationTests")]
public sealed class HealthIntegrationTests : IClassFixture<HealthTestFactory>
{
    private readonly HttpClient _client;

    public HealthIntegrationTests(HealthTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task GetHealth_WithoutAuth_Returns200()
    {
        var response = await _client.GetAsync("/api/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ReturnsJsonWithStatusAndDb()
    {
        var response = await _client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("status", out _).Should().BeTrue("response must have a 'status' field");
        root.TryGetProperty("db", out _).Should().BeTrue("response must have a 'db' field");
        root.TryGetProperty("cacheSize", out _).Should().BeTrue("response must have a 'cacheSize' field");
    }
}

public sealed class HealthTestFactory : WebApplicationFactory<Program>
{
    private const string SigningKey = "coldharbour-test-signing-key-32bytes!!";
    private const string Issuer = "coldharbour";
    private const string Audience = "coldharbour-web";

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

            services.RemoveAll<ILibraryReadRepository>();
            services.AddScoped<ILibraryReadRepository>(_ => new NullLibraryRepo());
            services.RemoveAll<ITrackRepository>();
            services.AddScoped<ITrackRepository>(_ => new NullTrackRepo());
            services.RemoveAll<ITrackIngestService>();
            services.AddScoped<ITrackIngestService>(_ => new NullIngest());
            services.RemoveAll<ILibraryReconciler>();
            services.AddScoped<ILibraryReconciler>(_ => new NullReconciler());
            services.RemoveAll<IArtworkService>();
            services.AddScoped<IArtworkService>(_ => new NullArtwork());

            services.RemoveAll<IDeviceRepository>();
            services.AddScoped<IDeviceRepository>(_ => new NullDeviceRepo());
            services.RemoveAll<ITranscodeService>();
            services.AddScoped<ITranscodeService>(_ => new NullTranscode());
            services.RemoveAll<IPlayEventRepository>();
            services.AddScoped<IPlayEventRepository>(_ => new NullPlayEventRepo());
            services.RemoveAll<IPlaybackSessionStore>();
            services.AddSingleton<IPlaybackSessionStore>(new ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore());

            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(new NullUserRepo());
            services.RemoveAll<IPasswordHasher>();
            services.AddSingleton<IPasswordHasher>(new PlainHasher());
            services.RemoveAll<ITokenService>();
            services.AddSingleton<ITokenService>(new StubTokenService());
            services.RemoveAll<IRefreshTokenRepository>();
            services.AddSingleton<IRefreshTokenRepository>(new NullRefreshTokenRepo());
        });
    }

    private sealed class NullLibraryRepo : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TrackReadModel>>([]);
    }

    private sealed class NullTrackRepo : ITrackRepository
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
        public Task<List<ColdHarbour.Domain.Library.Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default) => Task.FromResult(new List<ColdHarbour.Domain.Library.Track>());
    }

    private sealed class NullIngest : ITrackIngestService
    {
        public Task<ColdHarbour.Application.Library.Dtos.TrackUploadResultDto> IngestAsync(System.IO.Stream s, string f, CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
        public Task RemoveTrackFilesAsync(string? p, string sha1, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullReconciler : ILibraryReconciler
    {
        public Task<ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto([], [], []));
        public Task ApplyAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullArtwork : IArtworkService
    {
        public Task<string?> GetThumbnailPathAsync(Guid id, int size, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NullDeviceRepo : IDeviceRepository
    {
        public Task<ColdHarbour.Domain.Playback.Device?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.Device?>(null);
        public Task<IReadOnlyList<ColdHarbour.Domain.Playback.Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ColdHarbour.Domain.Playback.Device>>([]);
        public Task AddAsync(ColdHarbour.Domain.Playback.Device d, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullTranscode : ITranscodeService
    {
        public Task<string?> GetOrTranscodeAsync(string src, string sha1, string profile, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NullPlayEventRepo : IPlayEventRepository
    {
        public Task AddAsync(ColdHarbour.Domain.Playback.PlayEvent e, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ColdHarbour.Domain.Playback.PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.PlayEvent?>(null);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullUserRepo : IUserRepository
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
        public Task DeleteExpiredAndRevokedAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
