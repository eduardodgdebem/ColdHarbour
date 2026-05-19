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
}
