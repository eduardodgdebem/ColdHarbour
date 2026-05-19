namespace ColdHarbour.Domain.Playback;

public sealed class PlaybackSession
{
    public Guid UserId { get; private set; }
    public Guid? ActiveDeviceId { get; private set; }
    public Guid? TrackId { get; private set; }
    public long PositionMs { get; private set; }
    public bool IsPlaying { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PlaybackSession() { }

    public static PlaybackSession Create(Guid userId) =>
        new() { UserId = userId, UpdatedAt = DateTimeOffset.UtcNow };

    public void Start(Guid deviceId, Guid trackId)
    {
        ActiveDeviceId = deviceId;
        TrackId = trackId;
        PositionMs = 0;
        IsPlaying = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdatePosition(long positionMs)
    {
        PositionMs = positionMs;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Pause()
    {
        IsPlaying = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Resume()
    {
        if (TrackId is null)
            throw new InvalidOperationException("Cannot resume: no track is loaded.");
        IsPlaying = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Transfer(Guid newDeviceId, long positionMs)
    {
        if (TrackId is null)
            throw new InvalidOperationException("Cannot transfer: no track is loaded.");
        ActiveDeviceId = newDeviceId;
        PositionMs = positionMs;
        IsPlaying = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Clear()
    {
        ActiveDeviceId = null;
        TrackId = null;
        PositionMs = 0;
        IsPlaying = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
