using ColdHarbour.Application.Playback.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Jobs;

public sealed class DevicePruneJob(IServiceScopeFactory scopeFactory, ILogger<DevicePruneJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextDailyAt0430();
                logger.LogInformation("DevicePruneJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DevicePruneJob failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var count = await repo.DeleteStaleAsync(cutoff, ct);

        logger.LogInformation("Device prune complete: {Count} stale devices removed", count);
    }

    private static TimeSpan TimeUntilNextDailyAt0430()
    {
        // America/Sao_Paulo — simplified as UTC-3 for MVP.
        var nowUtc = DateTimeOffset.UtcNow;
        var nowLocal = nowUtc.ToOffset(TimeSpan.FromHours(-3));

        var nextRun = nowLocal.Date.AddHours(4).AddMinutes(30);
        if (nowLocal.DateTime >= nextRun)
            nextRun = nextRun.AddDays(1);

        var delay = nextRun - nowLocal.DateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromHours(1) : delay;
    }
}
