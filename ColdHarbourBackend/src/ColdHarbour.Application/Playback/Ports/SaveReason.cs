namespace ColdHarbour.Application.Playback.Ports;

/// <summary>
/// Reason a playback session snapshot is being persisted.
/// Informational today; load-bearing for a future Redis-with-optimistic-concurrency port.
/// </summary>
public enum SaveReason
{
    /// <summary>Actor loaded the session and immediately flushed it for visibility.</summary>
    Hydrate,

    /// <summary>A user-visible state change occurred (queue, seek, pause, transfer, …).</summary>
    MaterialChange,

    /// <summary>Heartbeat write that passed the actor-side throttle window.</summary>
    HeartbeatThrottled,

    /// <summary>Actor is being evicted or the host is shutting down.</summary>
    Shutdown,
}
