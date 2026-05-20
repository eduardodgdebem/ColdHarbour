using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Jobs;

public sealed class ArtCachePruneJob(IConfiguration config, ILogger<ArtCachePruneJob> logger) : BackgroundService
{
    private const long DefaultLimitBytes = 536_870_912L; // 512 MB

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextThursdayAt3_15Am();
                logger.LogInformation("ArtCachePruneJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ArtCachePruneJob failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        var contentRoot = config["COLDHARBOUR_CONTENT_ROOT"]
            ?? Path.Combine(Path.GetTempPath(), "coldharbour");
        var artDir = Path.Combine(contentRoot, "cache", "art");

        if (!Directory.Exists(artDir))
            return;

        var limitBytes = config.GetValue<long>("COLDHARBOUR_ART_CACHE_LIMIT_BYTES", DefaultLimitBytes);

        var files = new DirectoryInfo(artDir)
            .GetFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Name.Contains(".tmp"))
            .ToList();

        var totalBytes = files.Sum(f => f.Length);

        if (totalBytes <= limitBytes)
        {
            logger.LogInformation("ArtCachePruneJob: {TotalMb} MB used, under limit — nothing to prune",
                totalBytes / 1_048_576);
            return;
        }

        var toEvict = SelectToEvict(files, totalBytes, limitBytes);
        foreach (var file in toEvict)
        {
            if (ct.IsCancellationRequested) break;
            try { file.Delete(); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete art file {File}", file.Name); }
        }

        logger.LogInformation("ArtCachePruneJob: evicted {Count} file(s)", toEvict.Count);
        await Task.CompletedTask;
    }

    public static IReadOnlyList<FileInfo> SelectToEvict(
        IEnumerable<FileInfo> files, long totalBytes, long limitBytes)
    {
        var evict = new List<FileInfo>();
        foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc))
        {
            if (totalBytes <= limitBytes) break;
            evict.Add(file);
            totalBytes -= file.Length;
        }
        return evict;
    }

    private static TimeSpan TimeUntilNextThursdayAt3_15Am()
    {
        var nowLocal = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilThursday == 0 && (nowLocal.Hour > 3 || (nowLocal.Hour == 3 && nowLocal.Minute >= 15)))
            daysUntilThursday = 7;

        var nextRun = nowLocal.Date.AddDays(daysUntilThursday).AddHours(3).AddMinutes(15);
        var delay = nextRun - nowLocal.DateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromHours(1) : delay;
    }
}
