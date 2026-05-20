using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ColdHarbour.Infrastructure.Jobs;

public sealed class BackupJob(IConfiguration config, ILogger<BackupJob> logger) : BackgroundService
{
    private const int KeepCount = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextSaturdayAt3Am();
                logger.LogInformation("BackupJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BackupJob failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var contentRoot = config["COLDHARBOUR_CONTENT_ROOT"]
            ?? Path.Combine(Path.GetTempPath(), "coldharbour");
        var backupDir = Path.Combine(contentRoot, "backups");
        Directory.CreateDirectory(backupDir);

        var connStr = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not configured.");
        var builder = new NpgsqlConnectionStringBuilder(connStr);

        var fileName = $"coldharbour-{DateTimeOffset.UtcNow:yyyyMMdd}.backup";
        var outputPath = Path.Combine(backupDir, fileName);

        var args = $"--format=custom --file=\"{outputPath}\" --host={builder.Host} --port={builder.Port} --username={builder.Username} {builder.Database}";

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pg_dump",
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment = { ["PGPASSWORD"] = builder.Password ?? "" }
        };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            logger.LogError("BackupJob: pg_dump exited with code {Code}. {Stderr}", process.ExitCode, stderr);
            if (File.Exists(outputPath)) File.Delete(outputPath);
            return;
        }

        logger.LogInformation("BackupJob: wrote {File}", fileName);
        PruneOldBackups(backupDir);
    }

    private void PruneOldBackups(string backupDir)
    {
        var files = new DirectoryInfo(backupDir)
            .GetFiles("coldharbour-*.backup", SearchOption.TopDirectoryOnly)
            .ToList();

        var toDelete = SelectToDelete(files, KeepCount);
        foreach (var f in toDelete)
        {
            try { f.Delete(); logger.LogInformation("BackupJob: removed old backup {File}", f.Name); }
            catch (Exception ex) { logger.LogWarning(ex, "BackupJob: could not delete {File}", f.Name); }
        }
    }

    public static IReadOnlyList<FileInfo> SelectToDelete(IEnumerable<FileInfo> backupFiles, int keep)
        => backupFiles
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(keep)
            .ToList();

    private static TimeSpan TimeUntilNextSaturdayAt3Am()
    {
        var nowLocal = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilSaturday == 0 && nowLocal.Hour >= 3)
            daysUntilSaturday = 7;

        var nextRun = nowLocal.Date.AddDays(daysUntilSaturday).AddHours(3);
        var delay = nextRun - nowLocal.DateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromHours(1) : delay;
    }
}
