using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Library;

/// <summary>
/// Reproduces the "album splits into N copies" bug: the frontend uploads every
/// selected file in parallel, so concurrent ingests each find-or-create the same
/// artist/album. The CatalogLock must serialize them onto one artist + one album.
/// </summary>
public sealed class TrackIngestConcurrencyTests : IAsyncLifetime
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

    private TrackIngestService CreateService(ColdHarbourDbContext ctx)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_CONTENT_ROOT"] = Path.Combine(Path.GetTempPath(), $"ch-{Guid.NewGuid()}")
            })
            .Build();
        return new TrackIngestService(new TrackRepository(ctx), config, NullLogger<TrackIngestService>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ConcurrentEnsure_SameAlbum_CreatesExactlyOneArtistAndAlbum()
    {
        // Each "upload" gets its own DbContext + service (as in a scoped request),
        // all sharing the static CatalogLock. Fire them in parallel.
        var contexts = Enumerable.Range(0, 12).Select(_ => CreateContext()).ToList();
        try
        {
            var tasks = contexts.Select(ctx =>
                CreateService(ctx).EnsureArtistAndAlbumAsync(
                    "Daniel Caesar", "Freudian", 2017, artSha1: null, CancellationToken.None));

            await Task.WhenAll(tasks);

            await using var verify = CreateContext();
            (await verify.Artists.CountAsync(a => a.Name == "Daniel Caesar")).Should().Be(1);
            (await verify.Albums.CountAsync(a => a.Title == "Freudian")).Should().Be(1);
        }
        finally
        {
            foreach (var ctx in contexts)
                await ctx.DisposeAsync();
        }
    }
}
