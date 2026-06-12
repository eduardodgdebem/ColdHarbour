namespace ColdHarbour.Application.Playback;

/// <summary>
/// Tunable limits for hub-dispatched playback commands. Bound from configuration at the
/// composition root (<c>COLDHARBOUR_WS_MAX_QUEUE_SIZE</c>) and injected into the validators.
/// </summary>
public sealed class PlaybackLimits
{
    /// <summary>Maximum number of tracks accepted in a single <c>setQueue</c>. Default 1000.</summary>
    public int MaxQueueSize { get; init; } = 1000;

    /// <summary>
    /// How long the active device may be gone (no live socket) before the session releases it.
    /// Bound from <c>COLDHARBOUR_ACTIVE_DEVICE_TTL_SECONDS</c>. Default 30 seconds.
    /// </summary>
    public int ActiveDeviceTtlSeconds { get; init; } = 30;
}
