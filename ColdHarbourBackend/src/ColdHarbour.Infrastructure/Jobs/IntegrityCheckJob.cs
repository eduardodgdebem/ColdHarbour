using System.Security.Cryptography;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Jobs;

public sealed class IntegrityCheckJob(
    IServiceScopeFactory scopeFactory,
    IServiceProvider serviceProvider,
    ILogger<IntegrityCheckJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextSundayAt3Am();
                logger.LogInformation("IntegrityCheckJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);

                var config = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                var contentRoot = config["COLDHARBOUR_CONTENT_ROOT"]
                    ?? Path.Combine(Path.GetTempPath(), "coldharbour");

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ColdHarbourDbContext>();
                await RunAsync(db, contentRoot, stoppingToken);
                logger.LogInformation("IntegrityCheckJob: check complete");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IntegrityCheckJob failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    public static async Task RunAsync(ColdHarbourDbContext db, string contentRoot, CancellationToken ct)
    {
        var totalCount = await db.Tracks.CountAsync(t => t.Provider == "local", ct);
        if (totalCount == 0) return;

        var sampleSize = Math.Max(1, (int)Math.Ceiling(totalCount * 0.05));

        var allIds = await db.Tracks
            .Where(t => t.Provider == "local" && t.LocalPath != null)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var rng = new Random();
        var sampledIds = allIds.OrderBy(_ => rng.Next()).Take(sampleSize).ToHashSet();

        var tracks = await db.Tracks
            .Where(t => sampledIds.Contains(t.Id))
            .ToListAsync(ct);

        foreach (var track in tracks)
        {
            ct.ThrowIfCancellationRequested();
            var path = track.LocalPath!;

            if (!File.Exists(path))
            {
                track.FlagIntegrity("missing");
                continue;
            }

            var actualSha1 = await ComputeSha1Async(path, ct);
            track.FlagIntegrity(actualSha1 == track.AudioSha1 ? "ok" : "mismatch");
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var hashBytes = await SHA1.HashDataAsync(fs, ct);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static TimeSpan TimeUntilNextSundayAt3Am()
    {
        var nowLocal = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilSunday == 0 && nowLocal.Hour >= 3)
            daysUntilSunday = 7;

        var nextRun = nowLocal.Date.AddDays(daysUntilSunday).AddHours(3);
        var delay = nextRun - nowLocal.DateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromHours(1) : delay;
    }
}
