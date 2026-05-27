using System.Collections.Concurrent;
using System.Text.Json;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Playback;

/// <summary>
/// Postgres-backed implementation of <see cref="IPlaybackSessionStore"/>.
/// <para>
/// Hot-path reads come from a <see cref="ConcurrentDictionary{TKey,TValue}"/> that
/// is pre-warmed from the database in <see cref="StartAsync"/> (called by the .NET host
/// before the first request). After that, <see cref="GetOrCreate"/> is always a pure
/// cache operation; new users simply get a fresh session that is saved lazily once
/// <see cref="SaveAsync"/> is first called.
/// </para>
/// <para>
/// <b>Write strategy:</b> every material mutation (<c>isHeartbeat = false</c>) is
/// upserted immediately. Heartbeat writes are throttled to one write per
/// <see cref="HeartbeatThrottle"/> per user so the typical 2 s heartbeat cadence
/// does not saturate Postgres (~30 writes/min/user → ~1 write per 5 s).
/// </para>
/// </summary>
public sealed class PostgresPlaybackSessionStore : IPlaybackSessionStore, IHostedService
{
    private static readonly TimeSpan HeartbeatThrottle = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<PostgresPlaybackSessionStore> _logger;

    private readonly ConcurrentDictionary<Guid, PlaybackSession> _cache = new();

    // Last time a heartbeat write was persisted for each user.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastHeartbeatWrite = new();

    public PostgresPlaybackSessionStore(
        IServiceScopeFactory scopeFactory,
        Func<DateTimeOffset>? clock = null,
        ILogger<PostgresPlaybackSessionStore>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PostgresPlaybackSessionStore>.Instance;
    }

    // -----------------------------------------------------------------------
    // IHostedService — pre-warm cache on startup
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads all existing snapshots from Postgres into the hot cache.
    /// Any exception (e.g. DB not available in tests) is caught and logged so
    /// that the process continues with an empty cache — sessions will be created
    /// fresh and persisted on the first <see cref="SaveAsync"/> call.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ColdHarbourDbContext>();
            var snapshots = await db.PlaybackSessionSnapshots
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var snap in snapshots)
                _cache[snap.UserId] = FromSnapshot(snap);

            _logger.LogInformation(
                "PostgresPlaybackSessionStore: pre-warmed {Count} session(s) from database.",
                snapshots.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PostgresPlaybackSessionStore: could not pre-warm session cache from database. " +
                "Sessions will start fresh. This is expected in test environments without a live database.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // -----------------------------------------------------------------------
    // IPlaybackSessionStore
    // -----------------------------------------------------------------------

    public PlaybackSession GetOrCreate(Guid userId) =>
        _cache.GetOrAdd(userId, id => PlaybackSession.Create(id));

    public IReadOnlyList<PlaybackSession> GetAllForUser(Guid userId) =>
        _cache.TryGetValue(userId, out var session) ? [session] : [];

    public async Task SaveAsync(PlaybackSession session, bool isHeartbeat, CancellationToken ct = default)
    {
        if (isHeartbeat)
        {
            var lastWrite = _lastHeartbeatWrite.GetOrAdd(session.UserId, DateTimeOffset.MinValue);
            if (_clock() - lastWrite < HeartbeatThrottle)
                return; // throttled — skip this write
        }

        await UpsertAsync(session, ct);

        if (isHeartbeat)
            _lastHeartbeatWrite[session.UserId] = _clock();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task UpsertAsync(PlaybackSession session, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdHarbourDbContext>();

        var snap = await db.PlaybackSessionSnapshots.FindAsync([session.UserId], ct);
        if (snap is null)
        {
            snap = new PlaybackSessionSnapshot { UserId = session.UserId };
            db.PlaybackSessionSnapshots.Add(snap);
        }

        MapToSnapshot(session, snap);
        await db.SaveChangesAsync(ct);
    }

    private static void MapToSnapshot(PlaybackSession session, PlaybackSessionSnapshot snap)
    {
        snap.ActiveDeviceId = session.ActiveDeviceId;
        snap.TrackId = session.TrackId;
        snap.PositionMs = session.PositionMs;
        snap.IsPlaying = session.IsPlaying;
        snap.QueueJson = JsonSerializer.Serialize(session.Queue);
        snap.QueueIndex = session.QueueIndex;
        snap.RepeatMode = session.RepeatMode.ToString().ToLowerInvariant();
        snap.Shuffle = session.Shuffle;
        snap.UpdatedAt = session.UpdatedAt;
    }

    private static PlaybackSession FromSnapshot(PlaybackSessionSnapshot snap)
    {
        if (!Enum.TryParse<RepeatMode>(snap.RepeatMode, ignoreCase: true, out var mode))
            mode = RepeatMode.Off;

        var queue = JsonSerializer.Deserialize<List<Guid>>(snap.QueueJson) ?? [];

        return PlaybackSession.Restore(
            userId: snap.UserId,
            activeDeviceId: snap.ActiveDeviceId,
            trackId: snap.TrackId,
            positionMs: snap.PositionMs,
            isPlaying: snap.IsPlaying,
            queue: queue,
            queueIndex: snap.QueueIndex,
            repeatMode: mode,
            shuffle: snap.Shuffle,
            updatedAt: snap.UpdatedAt);
    }
}
