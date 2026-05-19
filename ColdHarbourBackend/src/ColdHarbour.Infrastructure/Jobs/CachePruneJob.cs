using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Jobs;

public sealed class CachePruneJob(IConfiguration config, ILogger<CachePruneJob> logger) : BackgroundService
{
    private const long DefaultLimitBytes = 5_368_709_120L; // 5 GB

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextThursdayAt3Am();
                logger.LogInformation("CachePruneJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CachePruneJob failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        var contentRoot = config["COLDHARBOUR_CONTENT_ROOT"]
            ?? Path.Combine(Path.GetTempPath(), "coldharbour");
        var cacheDir = Path.Combine(contentRoot, "cache", "transcodes");

        if (!Directory.Exists(cacheDir))
            return;

        var limitBytes = config.GetValue<long>("COLDHARBOUR_TRANSCODE_CACHE_LIMIT_BYTES", DefaultLimitBytes);

        var files = new DirectoryInfo(cacheDir)
            .GetFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Name.Contains(".tmp"))
            .OrderBy(f => f.LastAccessTimeUtc)
            .ToList();

        var totalBytes = files.Sum(f => f.Length);

        if (totalBytes <= limitBytes)
        {
            logger.LogInformation("CachePruneJob: {TotalMb} MB used, under limit — nothing to prune",
                totalBytes / 1_048_576);
            return;
        }

        var deleted = 0;
        foreach (var file in files)
        {
            if (totalBytes <= limitBytes || ct.IsCancellationRequested)
                break;
            try
            {
                totalBytes -= file.Length;
                file.Delete();
                deleted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete cache file {File}", file.Name);
            }
        }

        logger.LogInformation("CachePruneJob: deleted {Count} file(s)", deleted);
        await Task.CompletedTask;
    }

    private static TimeSpan TimeUntilNextThursdayAt3Am()
    {
        // Schedule in America/Sao_Paulo (UTC-3 / UTC-2 DST).
        // Simplified: just use UTC-3 offset for MVP.
        var nowUtc = DateTimeOffset.UtcNow;
        var nowLocal = nowUtc.ToOffset(TimeSpan.FromHours(-3));

        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilThursday == 0 && nowLocal.Hour >= 3)
            daysUntilThursday = 7;

        var nextRun = nowLocal.Date.AddDays(daysUntilThursday).AddHours(3);
        var delay = nextRun - nowLocal.DateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromHours(1) : delay;
    }
}
