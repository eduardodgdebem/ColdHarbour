using System.Collections.Concurrent;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Infrastructure.Playback;

/// <summary>
/// In-memory implementation of <see cref="IPlaybackSessionStore"/> for tests.
/// Each <see cref="LoadAsync"/> returns a clone so callers cannot alias the stored state.
/// </summary>
public sealed class InMemoryPlaybackSessionStore : IPlaybackSessionStore
{
    private readonly ConcurrentDictionary<Guid, PlaybackSession> _sessions = new();

    public Task<PlaybackSession?> LoadAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(userId, out var stored))
            return Task.FromResult<PlaybackSession?>(null);

        return Task.FromResult<PlaybackSession?>(Clone(stored));
    }

    public Task SaveAsync(PlaybackSession session, SaveReason reason, CancellationToken ct = default)
    {
        _sessions[session.UserId] = Clone(session);
        return Task.CompletedTask;
    }

    private static PlaybackSession Clone(PlaybackSession s) =>
        PlaybackSession.Restore(
            userId: s.UserId,
            activeDeviceId: s.ActiveDeviceId,
            trackId: s.TrackId,
            positionMs: s.PositionMs,
            isPlaying: s.IsPlaying,
            queue: s.Queue,
            queueIndex: s.QueueIndex,
            repeatMode: s.RepeatMode,
            shuffle: s.Shuffle,
            updatedAt: s.UpdatedAt,
            revision: s.Revision);
}
