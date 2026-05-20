using System.Security.Cryptography;
using ColdHarbour.Domain.Library;
using ColdHarbour.Infrastructure.Jobs;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Jobs;

public sealed class IntegrityCheckJobTests : IAsyncLifetime, IDisposable
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    private ColdHarbourDbContext CreateContext()
    {
        var opts = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new ColdHarbourDbContext(opts);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        Directory.CreateDirectory(_dir);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static string Sha1Hex(byte[] data)
    {
        var bytes = SHA1.HashData(data);
        return Convert.ToHexStringLower(bytes);
    }

    private async Task<Track> SeedTrackAsync(ColdHarbourDbContext ctx, string path, string audioSha1)
    {
        var artist = Artist.Create("Test Artist");
        var album = Album.Create("Test Album", artist.Id, 2024);
        var track = Track.Create("Test Track", album.Id, TimeSpan.FromSeconds(60),
            "local", "flac", 320, audioSha1, localPath: path);

        ctx.Artists.Add(artist);
        ctx.Albums.Add(album);
        ctx.Tracks.Add(track);
        await ctx.SaveChangesAsync();
        return track;
    }

    [Fact]
    public async Task RunAsync_WhenFileSha1Matches_FlagsOk()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var filePath = Path.Combine(_dir, "match.flac");
        await File.WriteAllBytesAsync(filePath, content);
        var sha1 = Sha1Hex(content);

        await using (var ctx = CreateContext())
            await SeedTrackAsync(ctx, filePath, sha1);

        await using (var ctx = CreateContext())
            await IntegrityCheckJob.RunAsync(ctx, _dir, CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            var track = await ctx.Tracks.FirstAsync();
            track.IntegrityStatus.Should().Be("ok");
        }
    }

    [Fact]
    public async Task RunAsync_WhenFileSha1Mismatches_FlagsMismatch()
    {
        var content = new byte[] { 10, 20, 30 };
        var filePath = Path.Combine(_dir, "mismatch.flac");
        await File.WriteAllBytesAsync(filePath, content);
        var wrongSha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        await using (var ctx = CreateContext())
            await SeedTrackAsync(ctx, filePath, wrongSha1);

        await using (var ctx = CreateContext())
            await IntegrityCheckJob.RunAsync(ctx, _dir, CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            var track = await ctx.Tracks.FirstAsync();
            track.IntegrityStatus.Should().Be("mismatch");
        }
    }

    [Fact]
    public async Task RunAsync_WhenFileMissing_FlagsMissing()
    {
        var sha1 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var missingPath = Path.Combine(_dir, "nonexistent.flac");

        await using (var ctx = CreateContext())
            await SeedTrackAsync(ctx, missingPath, sha1);

        await using (var ctx = CreateContext())
            await IntegrityCheckJob.RunAsync(ctx, _dir, CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            var track = await ctx.Tracks.FirstAsync();
            track.IntegrityStatus.Should().Be("missing");
        }
    }
}
