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

[Collection("IntegrationTests")]
public class AlbumArtistIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestSigningKey = "coldharbour-test-signing-key-32bytes!!";
    private const string TestIssuer = "coldharbour";
    private const string TestAudience = "coldharbour-web";

    private static readonly Guid ArtistId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid AlbumId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid TrackId = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private const string Sha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly AlbumReadModel Album = new(
        AlbumId, "The Wall", ArtistId, "Pink Floyd", 1979, Sha1, 1);

    private static readonly AlbumDetailReadModel AlbumDetail = new(
        AlbumId, "The Wall", ArtistId, "Pink Floyd", 1979, Sha1,
        [new TrackReadModel(TrackId, AlbumId, "Comfortably Numb", "Pink Floyd", "The Wall",
            TimeSpan.FromSeconds(382), "/x.flac", "flac", 900)]);

    private static readonly ArtistReadModel Artist = new(ArtistId, "Pink Floyd", 1);
    private static readonly ArtistDetailReadModel ArtistDetail = new(ArtistId, "Pink Floyd", [Album]);

    private static readonly string TempThumb = CreateTempWebp();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public AlbumArtistIntegrationTests(WebApplicationFactory<Program> factory)
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
                services.RemoveAll<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>();
                services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<
                    ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>));

                services.RemoveAll<ILibraryReadRepository>();
                services.AddScoped<ILibraryReadRepository>(_ => new FakeReadRepo());

                services.RemoveAll<IArtworkService>();
                services.AddScoped<IArtworkService>(_ => new FakeArtwork());

                services.RemoveAll<ITrackRepository>();
                services.AddScoped<ITrackRepository>(_ => new NoopTrackRepo());
                services.RemoveAll<ITrackIngestService>();
                services.AddScoped<ITrackIngestService>(_ => new NoopIngest());
                services.RemoveAll<ILibraryReconciler>();
                services.AddScoped<ILibraryReconciler>(_ => new NoopReconciler());

                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(new NoopUserRepo());
                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher>(new NoopHasher());
                services.RemoveAll<ITokenService>();
                services.AddSingleton<ITokenService>(new NoopTokens());
                services.RemoveAll<IRefreshTokenRepository>();
                services.AddSingleton<IRefreshTokenRepository>(new NoopRefresh());

                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IDeviceRepository>();
                services.AddScoped<ColdHarbour.Application.Playback.Ports.IDeviceRepository>(_ => new NoopDeviceRepo());
                services.RemoveAll<ColdHarbour.Application.Playback.Ports.ITranscodeService>();
                services.AddScoped<ColdHarbour.Application.Playback.Ports.ITranscodeService>(_ => new NoopTranscode());
                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IPlayEventRepository>();
                services.AddScoped<ColdHarbour.Application.Playback.Ports.IPlayEventRepository>(_ => new NoopPlayEvents());
                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IPlaybackSessionStore>();
                services.AddSingleton<ColdHarbour.Application.Playback.Ports.IPlaybackSessionStore>(
                    new ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore());
                services.RemoveAll<ColdHarbour.Application.Playback.Ports.IConnectedDeviceStore>();
                services.AddSingleton<ColdHarbour.Application.Playback.Ports.IConnectedDeviceStore>(new NoopConnected());
            });
        }).CreateClient();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTestToken());
    }

    private static string CreateTempWebp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ch-test-{Guid.NewGuid()}.webp");
        File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]); // "RIFF" — content irrelevant for header test
        return path;
    }

    private static string GenerateTestToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(TestIssuer, TestAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task GetAlbums_ReturnsListWithImageRefVersion()
    {
        var res = await _client.GetAsync("/api/albums");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var albums = JsonSerializer.Deserialize<List<AlbumSummaryResponse>>(
            await res.Content.ReadAsStringAsync(), Json)!;

        albums.Should().ContainSingle();
        albums[0].Title.Should().Be("The Wall");
        albums[0].ImageRef.Should().Be($"/api/artwork/{AlbumId}?size=256&v={Sha1}");
    }

    [Fact]
    public async Task GetAlbum_ReturnsDetailWithTracks()
    {
        var res = await _client.GetAsync($"/api/albums/{AlbumId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = JsonSerializer.Deserialize<AlbumDetailResponse>(
            await res.Content.ReadAsStringAsync(), Json)!;

        detail.Title.Should().Be("The Wall");
        detail.Tracks.Should().ContainSingle();
        detail.Tracks[0].AudioRef.Should().Be($"/api/stream/{TrackId}");
    }

    [Fact]
    public async Task GetAlbum_Returns404_WhenUnknown()
    {
        var res = await _client.GetAsync($"/api/albums/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetArtists_ReturnsList()
    {
        var res = await _client.GetAsync("/api/artists");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var artists = JsonSerializer.Deserialize<List<ArtistSummaryResponse>>(
            await res.Content.ReadAsStringAsync(), Json)!;

        artists.Should().ContainSingle(a => a.Name == "Pink Floyd" && a.AlbumCount == 1);
    }

    [Fact]
    public async Task GetArtist_Returns404_WhenUnknown()
    {
        var res = await _client.GetAsync($"/api/artists/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Artwork_ETag_IncludesCoverSha1()
    {
        var res = await _client.GetAsync($"/api/artwork/{AlbumId}?size=256");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.ETag!.Tag.Should().Be($"\"{AlbumId}-256-{Sha1}\"");
    }

    // ── responses ────────────────────────────────────────────────────────────────

    private sealed class AlbumSummaryResponse
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = "";
        public string ImageRef { get; init; } = "";
        public int TrackCount { get; init; }
    }

    private sealed class AlbumDetailResponse
    {
        public string Title { get; init; } = "";
        public List<MusicResponse> Tracks { get; init; } = [];
    }

    private sealed class MusicResponse
    {
        public string AudioRef { get; init; } = "";
        public string ImageRef { get; init; } = "";
    }

    private sealed class ArtistSummaryResponse
    {
        public string Name { get; init; } = "";
        public int AlbumCount { get; init; }
    }

    // ── stubs ────────────────────────────────────────────────────────────────────

    private sealed class FakeReadRepo : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TrackReadModel>>([]);
        public Task<IReadOnlyList<AlbumReadModel>> GetAlbumsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AlbumReadModel>>([Album]);
        public Task<AlbumDetailReadModel?> GetAlbumAsync(Guid albumId, CancellationToken ct = default)
            => Task.FromResult<AlbumDetailReadModel?>(albumId == AlbumId ? AlbumDetail : null);
        public Task<IReadOnlyList<ArtistReadModel>> GetArtistsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ArtistReadModel>>([Artist]);
        public Task<ArtistDetailReadModel?> GetArtistAsync(Guid artistId, CancellationToken ct = default)
            => Task.FromResult<ArtistDetailReadModel?>(artistId == ArtistId ? ArtistDetail : null);
    }

    private sealed class FakeArtwork : IArtworkService
    {
        public Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default)
            => Task.FromResult<string?>(albumId == AlbumId ? TempThumb : null);
        public Task<string?> GetCoverArtSha1Async(Guid albumId, CancellationToken ct = default)
            => Task.FromResult<string?>(albumId == AlbumId ? Sha1 : null);
    }

    private sealed class NoopTrackRepo : ITrackRepository
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

    private sealed class NoopIngest : ITrackIngestService
    {
        public Task<ColdHarbour.Application.Library.Dtos.TrackUploadResultDto> IngestAsync(Stream s, string f, CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
        public Task<ColdHarbour.Application.Library.Dtos.TrackUploadResultDto> IngestExistingFileAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
        public Task RemoveTrackFilesAsync(string? p, string sha1, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopReconciler : ILibraryReconciler
    {
        public Task<ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
            => Task.FromResult(new ColdHarbour.Application.Library.Dtos.LibrarySyncDiffDto([], [], []));
        public Task ApplyAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopUserRepo : IUserRepository
    {
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> AnyUsersExistAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopHasher : IPasswordHasher
    {
        public string Hash(string plaintext) => $"FAKE:{plaintext}";
        public bool Verify(string plaintext, string hash) => hash == $"FAKE:{plaintext}";
    }

    private sealed class NoopTokens : ITokenService
    {
        public string GenerateAccessToken(User user, string deviceId) => "stub";
        public string GenerateMediaToken(User user) => "stub-media";
        public string GenerateRefreshTokenPlaintext() => "stub-refresh";
    }

    private sealed class NoopRefresh : IRefreshTokenRepository
    {
        public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<RefreshToken?>(null);
        public Task AddAsync(RefreshToken token, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteExpiredAndRevokedAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopDeviceRepo : ColdHarbour.Application.Playback.Ports.IDeviceRepository
    {
        public Task<ColdHarbour.Domain.Playback.Device?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.Device?>(null);
        public Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<ColdHarbour.Domain.Playback.Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ColdHarbour.Domain.Playback.Device>>([]);
        public Task AddAsync(ColdHarbour.Domain.Playback.Device d, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoopTranscode : ColdHarbour.Application.Playback.Ports.ITranscodeService
    {
        public Task<string?> GetOrTranscodeAsync(string sourcePath, string audioSha1, string profile, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoopPlayEvents : ColdHarbour.Application.Playback.Ports.IPlayEventRepository
    {
        public Task AddAsync(ColdHarbour.Domain.Playback.PlayEvent e, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ColdHarbour.Domain.Playback.PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.PlayEvent?>(null);
        public Task<IReadOnlyList<ColdHarbour.Domain.Playback.PlayEvent>> FindOrphanedAsync(DateTimeOffset before, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ColdHarbour.Domain.Playback.PlayEvent>>(Array.Empty<ColdHarbour.Domain.Playback.PlayEvent>());
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopConnected : ColdHarbour.Application.Playback.Ports.IConnectedDeviceStore
    {
        public void Add(Guid deviceId) { }
        public void Remove(Guid deviceId) { }
        public IReadOnlySet<Guid> GetConnected() => new HashSet<Guid>();
    }
}
