using ColdHarbour.Domain.Library;
using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Library;

public class TrackRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ColdHarbourDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new ColdHarbourDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private const string Sha1A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sha1B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static async Task<(Artist artist, Album album)> SeedArtistAlbumAsync(
        ColdHarbourDbContext ctx, string artistName = "Pink Floyd", string albumTitle = "The Wall")
    {
        var artist = Artist.Create(artistName);
        var album = Album.Create(albumTitle, artist.Id, 1979);
        ctx.Artists.Add(artist);
        ctx.Albums.Add(album);
        await ctx.SaveChangesAsync();
        return (artist, album);
    }

    [Fact]
    public async Task FindByAudioSha1Async_ReturnsExistingTrack()
    {
        Guid trackId;

        await using (var ctx = CreateContext())
        {
            var (_, album) = await SeedArtistAlbumAsync(ctx);
            var track = Track.Create("Comfortably Numb", album.Id,
                TimeSpan.FromSeconds(382), "local", "flac", 900, Sha1A);
            ctx.Tracks.Add(track);
            await ctx.SaveChangesAsync();
            trackId = track.Id;
        }

        await using (var ctx = CreateContext())
        {
            var repo = new TrackRepository(ctx);
            var found = await repo.FindByAudioSha1Async(Sha1A);
            found.Should().NotBeNull();
            found!.Id.Should().Be(trackId);
        }
    }

    [Fact]
    public async Task FindByAudioSha1Async_ReturnsNull_WhenNotFound()
    {
        await using var ctx = CreateContext();
        var repo = new TrackRepository(ctx);

        var found = await repo.FindByAudioSha1Async(Sha1B);
        found.Should().BeNull();
    }

    [Fact]
    public async Task FindOrCreate_ArtistAndAlbum_ThenAddTrack()
    {
        await using (var ctx = CreateContext())
        {
            var repo = new TrackRepository(ctx);
            var artist = Artist.Create("New Artist");
            await repo.AddArtistAsync(artist);

            var album = Album.Create("New Album", artist.Id);
            await repo.AddAlbumAsync(album);

            var track = Track.Create("New Track", album.Id,
                TimeSpan.FromSeconds(200), "local", "mp3", 128, Sha1A);
            await repo.AddTrackAsync(track);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var tracks = await ctx.Tracks.ToListAsync();
            tracks.Should().HaveCount(1);
            tracks[0].AudioSha1.Should().Be(Sha1A);
        }
    }

    [Fact]
    public async Task RemoveTrack_CascadeCheck_CountDecreases()
    {
        Guid albumId;

        await using (var ctx = CreateContext())
        {
            var (_, album) = await SeedArtistAlbumAsync(ctx);
            albumId = album.Id;

            var track = Track.Create("Track 1", album.Id,
                TimeSpan.FromSeconds(200), "local", "mp3", 128, Sha1A);
            ctx.Tracks.Add(track);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new TrackRepository(ctx);
            var track = await repo.FindByAudioSha1Async(Sha1A);
            repo.RemoveTrack(track!);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new TrackRepository(ctx);
            var count = await repo.CountTracksByAlbumIdAsync(albumId);
            count.Should().Be(0);
        }
    }

    [Fact]
    public async Task CountAlbumsByArtistIdAsync_ReturnsCorrectCount()
    {
        Guid artistId;

        await using (var ctx = CreateContext())
        {
            var artist = Artist.Create("Solo Artist");
            var album1 = Album.Create("Album One", artist.Id);
            var album2 = Album.Create("Album Two", artist.Id);
            ctx.Artists.Add(artist);
            ctx.Albums.AddRange(album1, album2);
            await ctx.SaveChangesAsync();
            artistId = artist.Id;
        }

        await using (var ctx = CreateContext())
        {
            var repo = new TrackRepository(ctx);
            var count = await repo.CountAlbumsByArtistIdAsync(artistId);
            count.Should().Be(2);
        }
    }
}
