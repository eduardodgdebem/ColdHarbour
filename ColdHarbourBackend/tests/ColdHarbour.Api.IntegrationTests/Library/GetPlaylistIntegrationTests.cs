using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ColdHarbour.Api.IntegrationTests.Library;

public class GetPlaylistIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public GetPlaylistIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove the real DbContext so no Postgres connection is needed.
                services.RemoveAll<DbContextOptions<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>>();
                services.RemoveAll<ColdHarbour.Infrastructure.Persistence.ColdHarbourDbContext>();
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
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var playlist = JsonSerializer.Deserialize<PlaylistResponse>(json, options);

        playlist.Should().NotBeNull();
        playlist!.Id.Should().Be(1);
        playlist.Name.Should().Be("Here we go again");
        playlist.Musics.Should().HaveCount(2);
        playlist.Musics.Select(m => m.Name).Should().Contain("Baby You're Bad");
        playlist.Musics.Select(m => m.Name).Should().Contain("Liz");
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
