using ColdHarbour.Domain.Library;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Persistence;

public class DbContextTests : IAsyncLifetime
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
    public async Task MigrateAsync_AppliesCleanly_OnFreshDatabase()
    {
        // MigrateAsync already ran in InitializeAsync — verify the schema exists and is queryable.
        await using var context = CreateContext();

        var act = async () =>
        {
            await context.Artists.CountAsync();
            await context.Albums.CountAsync();
            await context.Tracks.CountAsync();
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CanSaveAndRetrieve_Artist()
    {
        var artist = Artist.Create("Pink Floyd");

        await using var writeCtx = CreateContext();
        writeCtx.Artists.Add(artist);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var retrieved = await readCtx.Artists.FindAsync(artist.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Pink Floyd");
    }

    [Fact]
    public async Task CanSaveAndRetrieve_Album_WithArtistFk()
    {
        var artist = Artist.Create("David Bowie");
        var album = Album.Create("Ziggy Stardust", artist.Id, 1972);

        await using var writeCtx = CreateContext();
        writeCtx.Artists.Add(artist);
        writeCtx.Albums.Add(album);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var retrieved = await readCtx.Albums.FindAsync(album.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Ziggy Stardust");
        retrieved.ArtistId.Should().Be(artist.Id);
    }

    [Fact]
    public async Task CanSaveAndRetrieve_Track_WithAlbumFk()
    {
        var artist = Artist.Create("Radiohead");
        var album = Album.Create("OK Computer", artist.Id, 1997);
        var track = Track.Create(
            title: "Karma Police",
            albumId: album.Id,
            duration: TimeSpan.FromSeconds(262),
            provider: "local",
            format: "flac",
            bitrate: 1411,
            audioSha1: "da39a3ee5e6b4b0d3255bfef95601890afd80709",
            localPath: "/content/library/Radiohead/OK Computer/Karma Police.flac");

        await using var writeCtx = CreateContext();
        writeCtx.Artists.Add(artist);
        writeCtx.Albums.Add(album);
        writeCtx.Tracks.Add(track);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var retrieved = await readCtx.Tracks.FindAsync(track.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Karma Police");
        retrieved.AudioSha1.Should().Be("da39a3ee5e6b4b0d3255bfef95601890afd80709");
        retrieved.Duration.Should().Be(TimeSpan.FromSeconds(262));
    }
}
