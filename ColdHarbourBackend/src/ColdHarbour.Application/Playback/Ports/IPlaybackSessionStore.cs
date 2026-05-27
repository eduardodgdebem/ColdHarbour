using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Ports;

public interface IPlaybackSessionStore
{
    PlaybackSession GetOrCreate(Guid userId);
    IReadOnlyList<PlaybackSession> GetAllForUser(Guid userId);

    /// <summary>
    /// Persist the current state of <paramref name="session"/>.
    /// Implementations may throttle writes when <paramref name="isHeartbeat"/>
    /// is <c>true</c> (heartbeats fire every 2 s; the Postgres impl skips writes
    /// that arrive within 5 s of the last heartbeat write for the same user).
    /// Material mutations (<c>isHeartbeat = false</c>) are always written.
    /// In-memory implementations may treat this as a no-op.
    /// </summary>
    Task SaveAsync(PlaybackSession session, bool isHeartbeat, CancellationToken ct = default);
}
