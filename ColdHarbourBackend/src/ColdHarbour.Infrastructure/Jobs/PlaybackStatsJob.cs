using ColdHarbour.Infrastructure.Persistence;
using ColdHarbour.Infrastructure.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Jobs;

public sealed class PlaybackStatsJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PlaybackStatsJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextFridayAt3Am();
                logger.LogInformation("PlaybackStatsJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ColdHarbourDbContext>();
                await RunAsync(db, stoppingToken);
                logger.LogInformation("PlaybackStatsJob: materialization complete");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PlaybackStatsJob failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    public static async Task RunAsync(ColdHarbourDbContext db, CancellationToken ct)
    {
        var weekStart = GetWeekStart(DateTimeOffset.UtcNow);
        var weekStartOffset = new DateTimeOffset(weekStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Use ListenedMs (pause-aware) instead of wall-clock (EndedAt - StartedAt).
        var events = await db.PlayEvents
            .Where(e => e.StartedAt >= weekStartOffset)
            .Select(e => new { e.TrackId, e.ListenedMs, hasEnded = e.EndedAt != null })
            .ToListAsync(ct);

        var grouped = events
            .GroupBy(e => e.TrackId)
            .Select(g => new
            {
                TrackId = g.Key,
                PlayCount = g.Count(),
                TotalMs = g.Sum(e => e.hasEnded ? e.ListenedMs : 0L)
            });

        foreach (var s in grouped)
        {
            var existing = await db.PlayStats.FindAsync([s.TrackId, weekStart], ct);
            if (existing is null)
            {
                db.PlayStats.Add(new PlayStats
                {
                    TrackId = s.TrackId,
                    WeekOf = weekStart,
                    PlayCount = s.PlayCount,
                    TotalMs = s.TotalMs
                });
            }
            else
            {
                existing.PlayCount = s.PlayCount;
                existing.TotalMs = s.TotalMs;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static DateOnly GetWeekStart(DateTimeOffset reference)
    {
        var date = DateOnly.FromDateTime(reference.UtcDateTime);
        var daysFromMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysFromMonday);
    }

    private static TimeSpan TimeUntilNextFridayAt3Am()
    {
        var nowLocal = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilFriday == 0 && nowLocal.Hour >= 3)
            daysUntilFriday = 7;

        var nextRun = nowLocal.Date.AddDays(daysUntilFriday).AddHours(3);
        var delay = nextRun - nowLocal.DateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromHours(1) : delay;
    }
}
