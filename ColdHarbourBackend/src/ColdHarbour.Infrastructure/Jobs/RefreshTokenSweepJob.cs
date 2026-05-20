using ColdHarbour.Application.Identity.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Jobs;

public sealed class RefreshTokenSweepJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshTokenSweepJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeUntilNextDailyAt4Am();
                logger.LogInformation("RefreshTokenSweepJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);

                await using var scope = scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
                await repo.DeleteExpiredAndRevokedAsync(stoppingToken);
                logger.LogInformation("RefreshTokenSweepJob: sweep complete");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RefreshTokenSweepJob failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private static TimeSpan TimeUntilNextDailyAt4Am()
    {
        var nowLocal = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var todayAt4 = nowLocal.Date.AddHours(4);
        var nextRun = nowLocal.DateTime < todayAt4 ? todayAt4 : todayAt4.AddDays(1);
        var delay = nextRun - nowLocal.DateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromHours(1) : delay;
    }
}
