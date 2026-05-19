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

    private PlayEvent() { }

    public static PlayEvent Begin(Guid userId, Guid deviceId, Guid trackId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DeviceId = deviceId,
        TrackId = trackId,
        StartedAt = DateTimeOffset.UtcNow,
    };

    public void Complete(long durationMs, long positionMs)
    {
        EndedAt = DateTimeOffset.UtcNow;
        CompletedRatio = durationMs > 0
            ? Math.Min(1.0, (double)positionMs / durationMs)
            : 0;
    }
}
