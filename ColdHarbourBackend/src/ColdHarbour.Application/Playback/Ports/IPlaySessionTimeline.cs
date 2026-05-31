namespace ColdHarbour.Application.Playback.Ports;

/// <summary>
/// Single entry-point for all PlayEvent open/close decisions.
/// Command handlers call this instead of touching IPlayEventRepository directly.
/// </summary>
public interface IPlaySessionTimeline
{
    /// <summary>
    /// Called whenever the playing track changes (Next, Previous, SetQueue, TrackEnded, RemoveFromQueue).
    /// Closes the open event for <paramref name="oldTrackId"/> (if any) and opens a fresh one for
    /// <paramref name="newTrackId"/> (if non-null).
    /// </summary>
    Task TrackChangedAsync(
        Guid userId,
        Guid deviceId,
        Guid? oldTrackId,
        int oldPositionMs,
        Guid? newTrackId,
        CancellationToken ct);

    /// <summary>
    /// Called when the active device changes (Transfer).
    /// Closes the open event (regardless of track continuity) and opens a fresh one attributed to
    /// <paramref name="newDeviceId"/> (if non-null) on the same track.
    /// </summary>
    Task ActiveDeviceChangedAsync(
        Guid userId,
        Guid? oldDeviceId,
        int oldPositionMs,
        Guid? newDeviceId,
        CancellationToken ct);

    /// <summary>
    /// Called when the session is cleared (Stop/ClearQueue).
    /// Closes any open event. No new event is opened.
    /// </summary>
    Task SessionClearedAsync(Guid userId, int oldPositionMs, CancellationToken ct);

    /// <summary>
    /// Called when the active device pauses playback.
    /// Calls <see cref="Domain.Playback.PlayEvent.PauseListening"/> on the open event (if any).
    /// </summary>
    Task PausedAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct);

    /// <summary>
    /// Called when the active device resumes playback.
    /// Calls <see cref="Domain.Playback.PlayEvent.ResumeListening"/> on the open event (if any).
    /// </summary>
    Task ResumedAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct);
}
