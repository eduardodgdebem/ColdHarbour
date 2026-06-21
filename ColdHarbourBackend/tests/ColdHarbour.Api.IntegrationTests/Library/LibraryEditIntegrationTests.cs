using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Domain.Identity;
using ColdHarbour.Domain.Library;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ColdHarbour.Api.IntegrationTests.Library;

[Collection("IntegrationTests")]
public class LibraryEditIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestSigningKey = "coldharbour-test-signing-key-32bytes!!";
    private const string TestIssuer = "coldharbour";
    private const string TestAudience = "coldharbour-web";

    private static readonly MutableLibraryRepo Repo = BuildRepo();
    private readonly HttpClient _client;

    private static MutableLibraryRepo BuildRepo()
    {
        var artist = Artist.Create("Pink Floyd");
        var album = Album.Create("The Wal", artist.Id, 1978);
        var track = Track.Create("Comfortably Num", album.Id, TimeSpan.FromSeconds(382),
            "local", "flac", 900, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", trackNumber: 5);
        return new MutableLibraryRepo(track, album, artist);
    }

    public LibraryEditIntegrationTests(WebApplicationFactory<Program> factory)
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

                services.RemoveAll<ITrackRepository>();
                services.AddScoped<ITrackRepository>(_ => Repo);
                services.RemoveAll<IArtworkService>();
                services.AddScoped<IArtworkService>(_ => new CoverArtwork());
                services.RemoveAll<ILibraryReadRepository>();
                services.AddScoped<ILibraryReadRepository>(_ => new EmptyReadRepo());
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
    public async Task PatchTrack_UpdatesTitle_Returns204()
    {
        var res = await _client.PatchAsJsonAsync($"/api/tracks/{Repo.Track.Id}",
            new { title = "Comfortably Numb", trackNumber = 6 });

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Repo.Track.Title.Should().Be("Comfortably Numb");
        Repo.Track.TrackNumber.Should().Be(6);
    }

    [Fact]
    public async Task PatchTrack_Returns404_WhenUnknown()
    {
        var res = await _client.PatchAsJsonAsync($"/api/tracks/{Guid.NewGuid()}",
            new { title = "X", trackNumber = (int?)null });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchTrack_Returns400_WhenTitleBlank()
    {
        var res = await _client.PatchAsJsonAsync($"/api/tracks/{Repo.Track.Id}",
            new { title = "", trackNumber = (int?)null });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PatchAlbum_UpdatesMetadata_Returns204()
    {
        var res = await _client.PatchAsJsonAsync($"/api/albums/{Repo.Album.Id}",
            new { title = "The Wall", year = 1979 });

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Repo.Album.Title.Should().Be("The Wall");
        Repo.Album.Year.Should().Be(1979);
    }

    [Fact]
    public async Task PatchAlbum_Returns404_WhenUnknown()
    {
        var res = await _client.PatchAsJsonAsync($"/api/albums/{Guid.NewGuid()}",
            new { title = "X", year = (int?)null });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchArtist_Renames_Returns204()
    {
        var res = await _client.PatchAsJsonAsync($"/api/artists/{Repo.Artist.Id}",
            new { name = "Pink Floyd!" });

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Repo.Artist.Name.Should().Be("Pink Floyd!");
    }

    [Fact]
    public async Task PatchArtist_Returns404_WhenUnknown()
    {
        var res = await _client.PatchAsJsonAsync($"/api/artists/{Guid.NewGuid()}",
            new { name = "X" });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostCover_Returns204_AndSetsSha1()
    {
        using var form = new MultipartFormDataContent();
        var img = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0x00]);
        img.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "file", "cover.jpg");

        var res = await _client.PostAsync($"/api/albums/{Repo.Album.Id}/cover", form);

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Repo.Album.CoverArtSha1.Should().Be("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    }

    [Fact]
    public async Task PostCover_Returns400_WhenNoFile()
    {
        using var form = new MultipartFormDataContent();
        var res = await _client.PostAsync($"/api/albums/{Repo.Album.Id}/cover", form);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── stubs ────────────────────────────────────────────────────────────────────

    private sealed class CoverArtwork : IArtworkService
    {
        public Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> GetCoverArtSha1Async(Guid albumId, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string> SaveSourceAsync(Stream content, string contentType, CancellationToken ct = default)
            => Task.FromResult("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    }

    private sealed class MutableLibraryRepo(Track track, Album album, Artist artist) : ITrackRepository
    {
        public Track Track { get; } = track;
        public Album Album { get; } = album;
        public Artist Artist { get; } = artist;

        public Task<Track?> FindByIdAsync(Guid trackId, CancellationToken ct = default)
            => Task.FromResult<Track?>(trackId == Track.Id ? Track : null);
        public Task<Album?> FindAlbumByIdAsync(Guid albumId, CancellationToken ct = default)
            => Task.FromResult<Album?>(albumId == Album.Id ? Album : null);
        public Task<Artist?> FindArtistByIdAsync(Guid artistId, CancellationToken ct = default)
            => Task.FromResult<Artist?>(artistId == Artist.Id ? Artist : null);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<Track?> FindByAudioSha1Async(string audioSha1, CancellationToken ct = default) => Task.FromResult<Track?>(null);
        public Task<Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
        public Task<Album?> FindAlbumByArtistAndTitleAsync(Guid artistId, string title, CancellationToken ct = default) => Task.FromResult<Album?>(null);
        public Task<int> CountTracksByAlbumIdAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> CountAlbumsByArtistIdAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddArtistAsync(Artist a, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddAlbumAsync(Album a, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddTrackAsync(Track t, CancellationToken ct = default) => Task.CompletedTask;
        public void RemoveTrack(Track t) { }
        public void RemoveAlbum(Album a) { }
        public void RemoveArtist(Artist a) { }
        public Task<List<Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default) => Task.FromResult(new List<Track>());
    }

    private sealed class EmptyReadRepo : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TrackReadModel>>([]);
        public Task<IReadOnlyList<AlbumReadModel>> GetAlbumsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AlbumReadModel>>([]);
        public Task<AlbumDetailReadModel?> GetAlbumAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult<AlbumDetailReadModel?>(null);
        public Task<IReadOnlyList<ArtistReadModel>> GetArtistsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ArtistReadModel>>([]);
        public Task<ArtistDetailReadModel?> GetArtistAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult<ArtistDetailReadModel?>(null);
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
