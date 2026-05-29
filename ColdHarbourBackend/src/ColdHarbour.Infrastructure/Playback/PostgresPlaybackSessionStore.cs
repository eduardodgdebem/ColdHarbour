using System.Text.Json;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ColdHarbour.Infrastructure.Playback;

/// <summary>
/// Postgres-backed implementation of <see cref="IPlaybackSessionStore"/>.
/// <para>
/// There is no in-memory cache — the actor that owns a user's session caches it
/// for the lifetime of that actor. <see cref="LoadAsync"/> reads from Postgres on
/// every call (at most once per actor lifetime in normal operation).
/// </para>
/// <para>
/// Heartbeat throttling is the actor's responsibility; the store persists every
/// call unconditionally. <see cref="SaveReason"/> is logged for observability.
/// </para>
/// </summary>
public sealed class PostgresPlaybackSessionStore : IPlaybackSessionStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgresPlaybackSessionStore> _logger;

    public PostgresPlaybackSessionStore(
        IServiceScopeFactory scopeFactory,
        ILogger<PostgresPlaybackSessionStore>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PostgresPlaybackSessionStore>.Instance;
    }

    public async Task<PlaybackSession?> LoadAsync(Guid userId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdHarbourDbContext>();

        var snap = await db.PlaybackSessionSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        return snap is null ? null : FromSnapshot(snap);
    }

    public async Task SaveAsync(PlaybackSession session, SaveReason reason, CancellationToken ct = default)
    {
        _logger.LogDebug("Saving playback session for user {UserId} ({Reason})", session.UserId, reason);
        await UpsertAsync(session, ct);
    }

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
        snap.Revision = session.Revision;
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
            updatedAt: snap.UpdatedAt,
            revision: snap.Revision);
    }
}
