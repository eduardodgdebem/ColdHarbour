using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Application.Library.Dtos;
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
public class LibraryIntegrationTests : IClassFixture<LibraryTestFactory>
{
    private readonly HttpClient _client;
    private readonly LibraryTestFactory _factory;

    public LibraryIntegrationTests(LibraryTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", LibraryTestFactory.GenerateToken());
    }

    // ── upload ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_WithNoFile_Returns400()
    {
        var content = new MultipartFormDataContent();
        var response = await _client.PostAsync("/api/library/tracks", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithValidFile_Returns201()
    {
        _factory.IngestService.SetResult(new TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), false));
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x49, 0x44, 0x33]), "file", "track.mp3");

        var response = await _client.PostAsync("/api/library/tracks", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Upload_WithDuplicateTrack_Returns200WithAlreadyExistedTrue()
    {
        _factory.IngestService.SetResult(new TrackUploadResultDto(Guid.NewGuid(), Guid.NewGuid(), true));
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x49, 0x44, 0x33]), "file", "dup.mp3");

        var response = await _client.PostAsync("/api/library/tracks", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        body.GetProperty("alreadyExisted").GetBoolean().Should().BeTrue();
    }

    // ── delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var id = Guid.NewGuid();
        var response = await _client.DeleteAsync($"/api/library/tracks/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── sync preview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncPreview_ReturnsOkWithDiff()
    {
        _factory.Reconciler.SetDiff(new LibrarySyncDiffDto(
            Added: [new LibrarySyncItemDto("/content/library/New Artist/New Album/new.mp3", "new", "New Artist")],
            Missing: [],
            Renamed: []));

        var response = await _client.GetAsync("/api/library/sync/preview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var diff = JsonSerializer.Deserialize<SyncDiffResponse>(json, options)!;
        diff.Added.Should().HaveCount(1);
    }

    // ── sync apply ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncApply_ReturnsNoContent()
    {
        var response = await _client.PostAsync("/api/library/sync", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Reconciler.ApplyCalled.Should().BeTrue();
    }

    // ── artwork ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Artwork_WhenNoArt_Returns404()
    {
        _factory.ArtworkService.SetPath(null);
        var response = await _client.GetAsync($"/api/artwork/{Guid.NewGuid()}?size=256");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── response models ───────────────────────────────────────────────────────────

    private sealed class SyncDiffResponse
    {
        public List<SyncItemResponse> Added { get; init; } = [];
        public List<SyncItemResponse> Missing { get; init; } = [];
        public List<SyncItemResponse> Renamed { get; init; } = [];
    }

    private sealed class SyncItemResponse
    {
        public string Path { get; init; } = "";
        public string? Title { get; init; }
        public string? Artist { get; init; }
    }
}

// ── test factory ─────────────────────────────────────────────────────────────────

public sealed class LibraryTestFactory : WebApplicationFactory<Program>
{
    private const string SigningKey = "coldharbour-test-signing-key-32bytes!!";
    private const string Issuer = "coldharbour";
    private const string Audience = "coldharbour-web";

    public readonly SpyTrackIngestService IngestService = new();
    public readonly SpyLibraryReconciler Reconciler = new();
    public readonly SpyArtworkService ArtworkService = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_JWT_SIGNING_KEY"] = SigningKey,
                ["COLDHARBOUR_JWT_ISSUER"] = Issuer,
                ["COLDHARBOUR_JWT_AUDIENCE"] = Audience,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove real Postgres
            services.RemoveAll<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>();
            services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.DbContextOptions<
                ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>));

            services.RemoveAll<ILibraryReadRepository>();
            services.AddScoped<ILibraryReadRepository>(_ => new EmptyLibraryReadRepo());

            services.RemoveAll<ITrackRepository>();
            services.AddScoped<ITrackRepository>(_ => new EmptyTrackRepo());

            services.RemoveAll<ITrackIngestService>();
            services.AddScoped<ITrackIngestService>(_ => IngestService);

            services.RemoveAll<ILibraryReconciler>();
            services.AddScoped<ILibraryReconciler>(_ => Reconciler);

            services.RemoveAll<IArtworkService>();
            services.AddScoped<IArtworkService>(_ => ArtworkService);

            // Identity stubs
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(new AlwaysExistsUserRepo());

            services.RemoveAll<IPasswordHasher>();
            services.AddSingleton<IPasswordHasher>(new PlainHasher());

            services.RemoveAll<ITokenService>();
            services.AddSingleton<ITokenService>(new StubTokenService());

            services.RemoveAll<IRefreshTokenRepository>();
            services.AddSingleton<IRefreshTokenRepository>(new NullRefreshTokenRepo());

            services.RemoveAll<ColdHarbour.Application.Playback.Ports.IDeviceRepository>();
            services.AddScoped<ColdHarbour.Application.Playback.Ports.IDeviceRepository>(_ => new NullDeviceRepo());

            services.RemoveAll<ColdHarbour.Application.Playback.Ports.ITranscodeService>();
            services.AddScoped<ColdHarbour.Application.Playback.Ports.ITranscodeService>(_ => new NullTranscodeService());
        });
    }

    public static string GenerateToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── spies ─────────────────────────────────────────────────────────────────────

    public sealed class SpyTrackIngestService : ITrackIngestService
    {
        private TrackUploadResultDto _result = new(Guid.NewGuid(), Guid.NewGuid(), false);
        public void SetResult(TrackUploadResultDto r) => _result = r;

        public Task<TrackUploadResultDto> IngestAsync(Stream fileStream, string fileName, CancellationToken ct = default)
            => Task.FromResult(_result);

        public Task RemoveTrackFilesAsync(string? localPath, string audioSha1, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    public sealed class SpyLibraryReconciler : ILibraryReconciler
    {
        private LibrarySyncDiffDto _diff = new([], [], []);
        public bool ApplyCalled { get; private set; }

        public void SetDiff(LibrarySyncDiffDto d) => _diff = d;

        public Task<LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
            => Task.FromResult(_diff);

        public Task ApplyAsync(CancellationToken ct = default)
        {
            ApplyCalled = true;
            return Task.CompletedTask;
        }
    }

    public sealed class SpyArtworkService : IArtworkService
    {
        private string? _path;
        public void SetPath(string? p) => _path = p;

        public Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default)
            => Task.FromResult(_path);
    }

    // ── minimal stubs ─────────────────────────────────────────────────────────────

    private sealed class EmptyLibraryReadRepo : ILibraryReadRepository
    {
        public Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TrackReadModel>>([]);
    }

    private sealed class EmptyTrackRepo : ITrackRepository
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
        public string Hash(string p) => $"FAKE:{p}";
        public bool Verify(string p, string h) => h == $"FAKE:{p}";
    }

    private sealed class StubTokenService : ITokenService
    {
        public string GenerateAccessToken(User user, string deviceId) => "stub";
        public string GenerateMediaToken(User user) => "stub-media";
        public string GenerateRefreshTokenPlaintext() => "stub-refresh";
    }

    private sealed class NullRefreshTokenRepo : IRefreshTokenRepository
    {
        public Task<RefreshToken?> FindByTokenHashAsync(string hash, CancellationToken ct = default) => Task.FromResult<RefreshToken?>(null);
        public Task AddAsync(RefreshToken token, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullDeviceRepo : ColdHarbour.Application.Playback.Ports.IDeviceRepository
    {
        public Task<ColdHarbour.Domain.Playback.Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default) => Task.FromResult<ColdHarbour.Domain.Playback.Device?>(null);
        public Task AddAsync(ColdHarbour.Domain.Playback.Device device, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullTranscodeService : ColdHarbour.Application.Playback.Ports.ITranscodeService
    {
        public Task<string?> GetOrTranscodeAsync(string sourcePath, string audioSha1, string profile, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
}
