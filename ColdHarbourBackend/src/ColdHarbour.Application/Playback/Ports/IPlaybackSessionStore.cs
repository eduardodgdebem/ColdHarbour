using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Ports;

public interface IPlaybackSessionStore
{
    /// <summary>
    /// Load the persisted snapshot for <paramref name="userId"/>.
    /// Returns <c>null</c> when no snapshot exists (first-time user); the caller
    /// is responsible for constructing a fresh <see cref="PlaybackSession"/>.
    /// Each call returns an independent clone — mutations do not bleed across calls.
    /// </summary>
    Task<PlaybackSession?> LoadAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Persist the current state of <paramref name="session"/>.
    /// The <paramref name="reason"/> is informational for logging and throttle
    /// decisions; the store persists unconditionally — throttling is the caller's
    /// responsibility (actor-side heartbeat gate).
    /// </summary>
    Task SaveAsync(PlaybackSession session, SaveReason reason, CancellationToken ct = default);
}
