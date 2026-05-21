using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Infrastructure.Identity;
using ColdHarbour.Infrastructure.Jobs;
using ColdHarbour.Infrastructure.Library;
using ColdHarbour.Infrastructure.Persistence;
using ColdHarbour.Infrastructure.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ColdHarbour.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<ColdHarbourDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("DefaultConnection")));

        services.AddScoped<ILibraryReadRepository, LibraryReadRepository>();
        services.AddScoped<ITrackRepository, TrackRepository>();
        services.AddScoped<ITrackIngestService, TrackIngestService>();
        services.AddScoped<ILibraryReconciler, LibraryReconciler>();
        services.AddScoped<IArtworkService, ArtworkService>();

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IPlayEventRepository, PlayEventRepository>();
        services.AddScoped<ITranscodeService, TranscodeService>();
        services.AddSingleton<IPlaybackSessionStore, InMemoryPlaybackSessionStore>();
        services.AddSingleton<InMemoryConnectedDeviceStore>();
        services.AddSingleton<IConnectedDeviceStore>(sp => sp.GetRequiredService<InMemoryConnectedDeviceStore>());
        services.AddHostedService<CachePruneJob>();
        services.AddHostedService<DevicePruneJob>();
        services.AddHostedService<ArtCachePruneJob>();
        services.AddHostedService<BackupJob>();
        services.AddHostedService<IntegrityCheckJob>();
        services.AddHostedService<PlaybackStatsJob>();
        services.AddHostedService<RefreshTokenSweepJob>();

        return services;
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<ColdHarbourDbContext>();
        if (db is null)
            return; // DbContext was not registered (e.g., integration test environment)
        await db.Database.MigrateAsync();
    }
}
