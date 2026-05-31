namespace ColdHarbour.Domain.Playback;

public sealed class PlayEvent
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid TrackId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public double? CompletedRatio { get; private set; }

    // Phase 3: pause-aware listened time
    /// <summary>Set when actively paused; null means currently playing.</summary>
    public DateTimeOffset? PausedAtUtc { get; private set; }
    /// <summary>Total milliseconds actually listened to (excludes paused time).</summary>
    public long ListenedMs { get; private set; }
    /// <summary>Start of the current listening segment; reset on each ResumeListening.</summary>
    public DateTimeOffset SegmentStartedAt { get; private set; }
    /// <summary>Set by the orphan-backfill command; null for normally-closed events.</summary>
    public DateTimeOffset? BackfilledAt { get; private set; }

    private PlayEvent() { }

    public static PlayEvent Begin(Guid userId, Guid deviceId, Guid trackId)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlayEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceId = deviceId,
            TrackId = trackId,
            StartedAt = now,
            SegmentStartedAt = now,
            ListenedMs = 0,
        };
    }

    /// <summary>
    /// Closes the current listening segment and marks the event as paused.
    /// Idempotent: calling on an already-paused event is a no-op.
    /// </summary>
    public void PauseListening(DateTimeOffset nowUtc)
    {
        if (PausedAtUtc.HasValue) return;
        var segmentMs = (long)(nowUtc - SegmentStartedAt).TotalMilliseconds;
        if (segmentMs > 0) ListenedMs += segmentMs;
        PausedAtUtc = nowUtc;
    }

    /// <summary>
    /// Resumes listening, starting a new segment from <paramref name="nowUtc"/>.
    /// Idempotent: calling on an already-active (non-paused) event is a no-op.
    /// </summary>
    public void ResumeListening(DateTimeOffset nowUtc)
    {
        if (!PausedAtUtc.HasValue) return;
        PausedAtUtc = null;
        SegmentStartedAt = nowUtc;
    }

    /// <summary>
    /// Heuristic close applied by the orphan-backfill command to events that were
    /// never closed normally (e.g. leaked before Phase 2 was deployed).
    /// Sets EndedAt, ListenedMs, and BackfilledAt directly — bypasses segment tracking.
    /// </summary>
    public void CloseAsOrphan(DateTimeOffset endedAt, long listenedMs, DateTimeOffset backfilledAt)
    {
        EndedAt = endedAt;
        ListenedMs = listenedMs;
        BackfilledAt = backfilledAt;
    }

    /// <summary>
    /// Closes the event. If actively listening, the final segment is accumulated into
    /// <see cref="ListenedMs"/>. Invariant: ListenedMs ≤ (EndedAt - StartedAt).
    /// </summary>
    public void Complete(long durationMs, long positionMs, DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        if (!PausedAtUtc.HasValue)
        {
            var segmentMs = (long)(now - SegmentStartedAt).TotalMilliseconds;
            if (segmentMs > 0) ListenedMs += segmentMs;
        }
        EndedAt = now;
        CompletedRatio = durationMs > 0
            ? Math.Min(1.0, (double)positionMs / durationMs)
            : 0;
    }
}
