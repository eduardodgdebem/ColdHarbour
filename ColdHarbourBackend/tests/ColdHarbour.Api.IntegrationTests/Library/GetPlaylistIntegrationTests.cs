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

namespace ColdHarbour.Api.IntegrationTests.Library;

// Same collection as AuthIntegrationTests so both run sequentially — they share
// WebApplicationFactory infrastructure that conflicts when running in parallel.
[Collection("IntegrationTests")]
public class GetPlaylistIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestSigningKey = "coldharbour-test-signing-key-32bytes!!";
    private const string TestIssuer = "coldharbour";
    private const string TestAudience = "coldharbour-web";

    private static readonly Guid AlbumId1 = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid AlbumId2 = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private static readonly IReadOnlyList<TrackReadModel> SeedTracks =
    [
        new TrackReadModel(
            Id: Guid.Parse("33333333-0000-0000-0000-000000000001"),
            AlbumId: AlbumId1,
            Title: "Baby You're Bad",
            ArtistName: "HONNE",
            AlbumTitle: "HONNE",
            Duration: TimeSpan.FromSeconds(210),
            LocalPath: "/assets/music/babyyourebad.mp3",
            Format: "mp3",
            Bitrate: 128),
        new TrackReadModel(
            Id: Guid.Parse("33333333-0000-0000-0000-000000000002"),
            AlbumId: AlbumId2,
            Title: "Liz",
            ArtistName: "Remi Wolf",
            AlbumTitle: "Remi Wolf",
            Duration: TimeSpan.FromSeconds(210),
            LocalPath: "/assets/music/liz.mp3",
            Format: "mp3",
            Bitrate: 128)
    ];

    private readonly HttpClient _client;

    public GetPlaylistIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["COLDHARBOUR_JWT_SIGNING_KEY"] = TestSigningKey,
                    ["COLDHARBOUR_JWT_ISSUER"] = TestIssuer,
                    ["COLDHARBOUR_JWT_AUDIENCE"] = TestAudience
                });
            });

            builder.ConfigureServices(services =>
            {
                // Remove the real DbContext and repository; register stubs so no
                // Postgres connection is needed in CI / local runs without Docker.
                services.RemoveAll<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>();
                services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<
                    ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>));

                services.RemoveAll<ILibraryReadRepository>();
                services.AddScoped<ILibraryReadRepository>(_ =>
                    new FakeLibraryReadRepository(SeedTracks));

                // Stub new library write-side ports so the container resolves them
                services.RemoveAll<ColdHarbour.Application.Library.Ports.ITrackRepository>();
                services.AddScoped<ColdHarbour.Application.Library.Ports.ITrackRepository>(_ =>
                    new FakeTrackRepository());

                services.RemoveAll<ColdHarbour.Application.Library.Ports.ITrackIngestService>();
                services.AddScoped<ColdHarbour.Application.Library.Ports.ITrackIngestService>(_ =>
                    new FakeTrackIngestService());

                services.RemoveAll<ColdHarbour.Application.Library.Ports.ILibraryReconciler>();
                services.AddScoped<ColdHarbour.Application.Library.Ports.ILibraryReconciler>(_ =>
                    new FakeLibraryReconciler());

                services.RemoveAll<ColdHarbour.Application.Library.Ports.IArtworkService>();
                services.AddScoped<ColdHarbour.Application.Library.Ports.IArtworkService>(_ =>
                    new FakeArtworkService());

                // Stub identity ports so startup bootstrap code doesn't fail
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(new FakeUserRepository());

                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher>(new FakePasswordHasher());

                services.RemoveAll<ITokenService>();
                services.AddSingleton<ITokenService>(new FakeTokenService());

                services.RemoveAll<IRefreshTokenRepository>();
                services.AddSingleton<IRefreshTokenRepository>(new FakeRefreshTokenRepository());

                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IDeviceRepository>();
                services.AddScoped<ColdHarbour.Application.Playback.Ports.IDeviceRepository>(_ => new NullDeviceRepo());

                services.RemoveAll<ColdHarbour.Application.Playback.Ports.ITranscodeService>();
                services.AddScoped<ColdHarbour.Application.Playback.Ports.ITranscodeService>(_ => new NullTranscodeService());
                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IPlayEventRepository>();
                services.AddScoped<ColdHarbour.Application.Playback.Ports.IPlayEventRepository>(_ => new NullPlayEventRepo());
                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IPlaybackSessionStore>();
                services.AddSingleton<ColdHarbour.Application.Playback.Ports.IPlaybackSessionStore>(new ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore());

                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IConnectedDeviceStore>();
                services.AddSingleton<ColdHarbour.Application.Playback.Ports.IConnectedDeviceStore>(new NullConnectedDeviceStore());
            });
        }).CreateClient();

        // Attach a valid bearer token to all requests from this client
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTestToken());
    }

    private static string GenerateTestToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task GetPlaylist_ReturnsOkWithExpectedPlaylist()
    {
        // Act
        var response = await _client.GetAsync("/api/music/playlist/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var playlist = JsonSerializer.Deserialize<PlaylistResponse>(json, options);

        playlist.Should().NotBeNull();
        playlist!.Id.Should().Be(1);
        playlist.Name.Should().Be("Library");
        playlist.Musics.Should().HaveCount(2);
        playlist.Musics.Select(m => m.Name).Should().Contain("Baby You're Bad");
        playlist.Musics.Select(m => m.Name).Should().Contain("Liz");
    }

    [Fact]
    public async Task GetPlaylist_MusicsHaveStreamAudioRef()
    {
        var response = await _client.GetAsync("/api/music/playlist/1");
        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var playlist = JsonSerializer.Deserialize<PlaylistResponse>(json, options)!;

        playlist.Musics.Should().AllSatisfy(m => m.AudioRef.Should().StartWith("/api/stream/"));
    }

    [Fact]
    public async Task GetPlaylist_MusicsHaveArtworkImageRef()
    {
        var response = await _client.GetAsync("/api/music/playlist/1");
        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var playlist = JsonSerializer.Deserialize<PlaylistResponse>(json, options)!;

        playlist.Musics.Should().AllSatisfy(m => m.ImageRef.Should().StartWith("/api/artwork/"));
    }

    // --- hand-crafted stubs ---

    private sealed class FakeLibraryReadRepository(IReadOnlyList<TrackReadModel> tracks)
        : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult(tracks);
    }

    private sealed class PlaylistResponse
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string ImageRef { get; init; } = "";
        public List<MusicResponse> Musics { get; init; } = [];
    }

    private sealed class MusicResponse
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string Author { get; init; } = "";
        public string AudioRef { get; init; } = "";
        public string ImageRef { get; init; } = "";
    }

    // ── minimal identity stubs needed so app startup (bootstrap owner) succeeds ──

    private sealed class FakeUserRepository : IUserRepository
    {
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> AnyUsersExistAsync(CancellationToken ct = default) => Task.FromResult(true); // skip bootstrap
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string plaintext) => $"FAKE:{plaintext}";
        public bool Verify(string plaintext, string hash) => hash == $"FAKE:{plaintext}";
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string GenerateAccessToken(User user, string deviceId) => "stub";
        public string GenerateMediaToken(User user) => "stub-media";
        public string GenerateRefreshTokenPlaintext() => "stub-refresh";
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<RefreshToken?>(null);
        public Task AddAsync(RefreshToken token, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteExpiredAndRevokedAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeTrackRepository : ColdHarbour.Application.Library.Ports.ITrackRepository
    {
        public Task<ColdHarbour.Domain.Library.Track?> FindByIdAsync(Guid trackId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Track?>(null);
        public Task<ColdHarbour.Domain.Library.Track?> FindByAudioSha1Async(string audioSha1, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Track?>(null);
        public Task<ColdHarbour.Domain.Library.Artist?> FindArtistByIdAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Artist?>(null);
        public Task<ColdHarbour.Domain.Library.Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Artist?>(null);
        public Task<ColdHarbour.Domain.Library.Album?> FindAlbumByArtistAndTitleAsync(Guid artistId, string title, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Album?>(null);
        public Task<ColdHarbour.Domain.Library.Album?> FindAlbumByIdAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Library.Album?>(null);
        public Task<int> CountTracksByAlbumIdAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> CountAlbumsByArtistIdAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddArtistAsync(ColdHarbour.Domain.Library.Artist artist, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddAlbumAsync(ColdHarbour.Domain.Library.Album album, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddTrackAsync(ColdHarbour.Domain.Library.Track track, CancellationToken ct = default) => Task.CompletedTask;
        public void RemoveTrack(ColdHarbour.Domain.Library.Track track) { }
        public void RemoveAlbum(ColdHarbour.Domain.Library.Album album) { }
        public void RemoveArtist(ColdHarbour.Domain.Library.Artist artist) { }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<ColdHarbour.Domain.Library.Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default)
            => Task.FromResult(new List<ColdHarbour.Domain.Library.Track>());
    }

    private sealed class FakeTrackIngestService : ColdHarbour.Application.Library.Ports.ITrackIngestService
    {
        public Task<ColdHarbour.Application.Library.Dtos.TrackUploadResultDto> IngestAsync(Stream fileStream, string fileName, CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
        public Task<ColdHarbour.Application.Library.Dtos.TrackUploadResultDto> IngestExistingFileAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
        public Task RemoveTrackFilesAsync(string? localPath, string audioSha1, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeLibraryReconciler : ColdHarbour.Application.Library.Ports.ILibraryReconciler
    {
        public Task<ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto([], [], []));
        public Task ApplyAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeArtworkService : ColdHarbour.Application.Library.Ports.IArtworkService
    {
        public Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NullDeviceRepo : ColdHarbour.Application.Playback.Ports.IDeviceRepository
    {
        public Task<ColdHarbour.Domain.Playback.Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.Device?>(null);
        public Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<ColdHarbour.Domain.Playback.Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ColdHarbour.Domain.Playback.Device>>([]);
        public Task AddAsync(ColdHarbour.Domain.Playback.Device device, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NullConnectedDeviceStore : ColdHarbour.Application.Playback.Ports.IConnectedDeviceStore
    {
        public void Add(Guid deviceId) { }
        public void Remove(Guid deviceId) { }
        public IReadOnlySet<Guid> GetConnected() => new HashSet<Guid>();
    }

    private sealed class NullTranscodeService : ColdHarbour.Application.Playback.Ports.ITranscodeService
    {
        public Task<string?> GetOrTranscodeAsync(string sourcePath, string audioSha1, string profile, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NullPlayEventRepo : ColdHarbour.Application.Playback.Ports.IPlayEventRepository
    {
        public Task AddAsync(ColdHarbour.Domain.Playback.PlayEvent e, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ColdHarbour.Domain.Playback.PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.PlayEvent?>(null);
        public Task<IReadOnlyList<ColdHarbour.Domain.Playback.PlayEvent>> FindOrphanedAsync(DateTimeOffset before, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ColdHarbour.Domain.Playback.PlayEvent>>(Array.Empty<ColdHarbour.Domain.Playback.PlayEvent>());
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
