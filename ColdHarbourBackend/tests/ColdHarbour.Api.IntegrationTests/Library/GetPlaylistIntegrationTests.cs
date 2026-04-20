using System.Net;
using System.Text.Json;
using ColdHarbour.Application.Library.Ports;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ColdHarbour.Api.IntegrationTests.Library;

public class GetPlaylistIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
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
            });
        }).CreateClient();
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
}
