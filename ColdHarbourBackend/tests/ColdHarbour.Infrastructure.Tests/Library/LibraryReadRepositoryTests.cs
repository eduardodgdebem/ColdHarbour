using ColdHarbour.Domain.Library;
using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Library;

public class LibraryReadRepositoryTests : IAsyncLifetime
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

    [Fact]
    public async Task GetAllTracksAsync_ReturnsEmpty_WhenNoTracksExist()
    {
        await using var context = CreateContext();
        var repo = new LibraryReadRepository(context);

        var tracks = await repo.GetAllTracksAsync();

        tracks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllTracksAsync_ReturnsTracksWithAllFields()
    {
        var sha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        await using (var ctx = CreateContext())
        {
            var artist = Artist.Create("Pink Floyd");
            var album = Album.Create("The Wall", artist.Id, 1979);
            var track = Track.Create("Comfortably Numb", album.Id,
                TimeSpan.FromSeconds(382), "local", "flac", 900, sha1,
                localPath: "/content/library/Pink Floyd/The Wall/comfortably_numb.flac",
                trackNumber: 6);

            ctx.Artists.Add(artist);
            ctx.Albums.Add(album);
            ctx.Tracks.Add(track);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new LibraryReadRepository(ctx);
            var tracks = await repo.GetAllTracksAsync();

            tracks.Should().HaveCount(1);
            var t = tracks[0];
            t.Title.Should().Be("Comfortably Numb");
            t.ArtistName.Should().Be("Pink Floyd");
            t.AlbumTitle.Should().Be("The Wall");
            t.Duration.Should().Be(TimeSpan.FromSeconds(382));
            t.LocalPath.Should().Contain("comfortably_numb");
            t.AlbumId.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task GetAlbumsAsync_ReturnsAlbumWithArtistYearCoverAndTrackCount()
    {
        var sha1 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        Guid albumId;

        await using (var ctx = CreateContext())
        {
            var artist = Artist.Create("Pink Floyd");
            var album = Album.Create("The Wall", artist.Id, 1979);
            album.UpdateCoverArt(sha1);
            albumId = album.Id;
            ctx.Artists.Add(artist);
            ctx.Albums.Add(album);
            ctx.Tracks.Add(Track.Create("Hey You", album.Id, TimeSpan.FromSeconds(280), "local", "flac", 900,
                "cccccccccccccccccccccccccccccccccccccccc", trackNumber: 1));
            ctx.Tracks.Add(Track.Create("Run Like Hell", album.Id, TimeSpan.FromSeconds(260), "local", "flac", 900,
                "dddddddddddddddddddddddddddddddddddddddd", trackNumber: 2));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new LibraryReadRepository(ctx);
            var albums = await repo.GetAlbumsAsync();

            albums.Should().ContainSingle();
            var a = albums[0];
            a.Id.Should().Be(albumId);
            a.Title.Should().Be("The Wall");
            a.ArtistName.Should().Be("Pink Floyd");
            a.Year.Should().Be(1979);
            a.CoverArtSha1.Should().Be(sha1);
            a.TrackCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task GetAlbumAsync_ReturnsDetailWithTracksOrderedByTrackNumber()
    {
        Guid albumId;

        await using (var ctx = CreateContext())
        {
            var artist = Artist.Create("Pink Floyd");
            var album = Album.Create("The Wall", artist.Id, 1979);
            albumId = album.Id;
            ctx.Artists.Add(artist);
            ctx.Albums.Add(album);
            ctx.Tracks.Add(Track.Create("Second", album.Id, TimeSpan.FromSeconds(200), "local", "flac", 900,
                "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", trackNumber: 2));
            ctx.Tracks.Add(Track.Create("First", album.Id, TimeSpan.FromSeconds(200), "local", "flac", 900,
                "ffffffffffffffffffffffffffffffffffffffff", trackNumber: 1));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new LibraryReadRepository(ctx);
            var detail = await repo.GetAlbumAsync(albumId);

            detail.Should().NotBeNull();
            detail!.ArtistName.Should().Be("Pink Floyd");
            detail.Tracks.Select(t => t.Title).Should().ContainInOrder("First", "Second");
        }
    }

    [Fact]
    public async Task GetAlbumAsync_ReturnsNull_WhenMissing()
    {
        await using var ctx = CreateContext();
        var repo = new LibraryReadRepository(ctx);

        (await repo.GetAlbumAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetArtistsAsync_ReturnsArtistWithAlbumCount()
    {
        Guid artistId;

        await using (var ctx = CreateContext())
        {
            var artist = Artist.Create("Radiohead");
            artistId = artist.Id;
            ctx.Artists.Add(artist);
            ctx.Albums.Add(Album.Create("OK Computer", artist.Id, 1997));
            ctx.Albums.Add(Album.Create("In Rainbows", artist.Id, 2007));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new LibraryReadRepository(ctx);
            var artists = await repo.GetArtistsAsync();

            artists.Should().ContainSingle();
            artists[0].Id.Should().Be(artistId);
            artists[0].Name.Should().Be("Radiohead");
            artists[0].AlbumCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task GetArtistAsync_ReturnsDetailWithAlbums()
    {
        Guid artistId;

        await using (var ctx = CreateContext())
        {
            var artist = Artist.Create("Radiohead");
            artistId = artist.Id;
            ctx.Artists.Add(artist);
            ctx.Albums.Add(Album.Create("OK Computer", artist.Id, 1997));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new LibraryReadRepository(ctx);
            var detail = await repo.GetArtistAsync(artistId);

            detail.Should().NotBeNull();
            detail!.Name.Should().Be("Radiohead");
            detail.Albums.Should().ContainSingle(a => a.Title == "OK Computer");
        }
    }

    [Fact]
    public async Task GetArtistAsync_ReturnsNull_WhenMissing()
    {
        await using var ctx = CreateContext();
        var repo = new LibraryReadRepository(ctx);

        (await repo.GetArtistAsync(Guid.NewGuid())).Should().BeNull();
    }
}
