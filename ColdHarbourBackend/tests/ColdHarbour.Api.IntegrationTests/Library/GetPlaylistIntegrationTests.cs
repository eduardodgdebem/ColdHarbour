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

    private static readonly IReadOnlyList<TrackReadModel> SeedTracks =
    [
        new TrackReadModel(
            Id: Guid.Parse("33333333-0000-0000-0000-000000000001"),
            Title: "Baby You're Bad",
            ArtistName: "HONNE",
            LocalPath: "/assets/music/babyyourebad.mp3",
            Format: "mp3",
            Bitrate: 128),
        new TrackReadModel(
            Id: Guid.Parse("33333333-0000-0000-0000-000000000002"),
            Title: "Liz",
            ArtistName: "Remi Wolf",
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

                // Stub identity ports so startup bootstrap code doesn't fail
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(new FakeUserRepository());

                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher>(new FakePasswordHasher());

                services.RemoveAll<ITokenService>();
                services.AddSingleton<ITokenService>(new FakeTokenService());

                services.RemoveAll<IRefreshTokenRepository>();
                services.AddSingleton<IRefreshTokenRepository>(new FakeRefreshTokenRepository());
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

    // --- hand-crafted stub (no mocking library) ---

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
        public string GenerateRefreshTokenPlaintext() => "stub-refresh";
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<RefreshToken?>(null);
        public Task AddAsync(RefreshToken token, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
