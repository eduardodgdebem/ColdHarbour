using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Jobs;
using ColdHarbour.Infrastructure.Persistence;
using ColdHarbour.Infrastructure.Playback;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Jobs;

public sealed class PlaybackStatsJobTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

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
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static PlayEvent MakeEvent(Guid trackId, DateTimeOffset startedAt, double durationSeconds = 120)
    {
        var durationMs = (long)(durationSeconds * 1000);
        var endedAt = startedAt.AddSeconds(durationSeconds);

        var e = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), trackId);

        // Use explicit nowUtc so ListenedMs is computed correctly relative to startedAt.
        typeof(PlayEvent).GetProperty("SegmentStartedAt")!.SetValue(e, startedAt);
        typeof(PlayEvent).GetProperty("StartedAt")!.SetValue(e, startedAt);

        e.Complete(durationMs, durationMs, endedAt);
        return e;
    }

    [Fact]
    public async Task RunAsync_MaterializesWeeklyPlayCounts()
    {
        var trackId = Guid.NewGuid();

        await using (var ctx = CreateContext())
        {
            // 3 events for this track; StartedAt defaults to UtcNow which is in the current week
            ctx.PlayEvents.AddRange(
                PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), trackId),
                PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), trackId),
                PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), trackId));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
            await PlaybackStatsJob.RunAsync(ctx, CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            var stats = await ctx.PlayStats.Where(s => s.TrackId == trackId).ToListAsync();
            stats.Should().ContainSingle();
            stats[0].PlayCount.Should().Be(3);
        }
    }

    [Fact]
    public async Task RunAsync_IsIdempotent_TwoRunsYieldIdenticalAggregates()
    {
        var trackId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1); // inside current week

        await using (var ctx = CreateContext())
        {
            ctx.PlayEvents.Add(MakeEvent(trackId, startedAt, durationSeconds: 180));
            ctx.PlayEvents.Add(MakeEvent(trackId, startedAt.AddMinutes(5), durationSeconds: 120));
            await ctx.SaveChangesAsync();
        }

        // First run
        await using (var ctx = CreateContext())
            await PlaybackStatsJob.RunAsync(ctx, CancellationToken.None);

        long totalMsAfterFirst;
        int playCountAfterFirst;
        await using (var ctx = CreateContext())
        {
            var stats = await ctx.PlayStats.Where(s => s.TrackId == trackId).ToListAsync();
            stats.Should().ContainSingle();
            playCountAfterFirst = stats[0].PlayCount;
            totalMsAfterFirst = stats[0].TotalMs;
        }

        // Second run — same data, no changes
        await using (var ctx = CreateContext())
            await PlaybackStatsJob.RunAsync(ctx, CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            var stats = await ctx.PlayStats.Where(s => s.TrackId == trackId).ToListAsync();
            stats.Should().ContainSingle();
            stats[0].PlayCount.Should().Be(playCountAfterFirst,
                "play count must be identical after a second consecutive run");
            stats[0].TotalMs.Should().Be(totalMsAfterFirst,
                "TotalMs must be identical after a second consecutive run");
        }
    }

    [Fact]
    public async Task RunAsync_UpsertsExistingStats()
    {
        var trackId = Guid.NewGuid();
        var weekStart = PlaybackStatsJob.GetWeekStart(DateTimeOffset.UtcNow);

        // Seed an existing stats row for the same week
        await using (var ctx = CreateContext())
        {
            ctx.PlayStats.Add(new PlayStats { TrackId = trackId, WeekOf = weekStart, PlayCount = 1, TotalMs = 60_000 });
            ctx.PlayEvents.Add(PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), trackId));
            ctx.PlayEvents.Add(PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), trackId));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
            await PlaybackStatsJob.RunAsync(ctx, CancellationToken.None);

        await using (var ctx = CreateContext())
        {
            var stats = await ctx.PlayStats.Where(s => s.TrackId == trackId).ToListAsync();
            stats.Should().ContainSingle();
            // Should be overwritten with the actual 2-event count (not the old 1)
            stats[0].PlayCount.Should().Be(2);
        }
    }
}
