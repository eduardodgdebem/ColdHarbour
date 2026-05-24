namespace ColdHarbour.Domain.Playback;

public sealed class PlaybackSession
{
    public Guid UserId { get; private set; }
    public Guid? ActiveDeviceId { get; private set; }
    public Guid? TrackId { get; private set; }
    public long PositionMs { get; private set; }
    public bool IsPlaying { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<Guid> _queue = [];
    public IReadOnlyList<Guid> Queue => _queue;
    public int QueueIndex { get; private set; }

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
        _queue.Clear();
        QueueIndex = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetQueue(IReadOnlyList<Guid> trackIds, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(trackIds);

        if (trackIds.Count == 0)
        {
            if (startIndex != 0)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be 0 when queue is empty.");
        }
        else if ((uint)startIndex >= (uint)trackIds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex), $"Start index {startIndex} is outside the queue range [0, {trackIds.Count - 1}].");
        }

        _queue.Clear();
        _queue.AddRange(trackIds);
        QueueIndex = startIndex;
        PositionMs = 0;

        if (_queue.Count > 0)
        {
            TrackId = _queue[startIndex];
            IsPlaying = true;
        }
        else
        {
            TrackId = null;
            IsPlaying = false;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MoveTo(int index)
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("Cannot move to an index in an empty queue.");

        if ((uint)index >= (uint)_queue.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is outside the queue range [0, {_queue.Count - 1}].");

        QueueIndex = index;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AdvanceNext()
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("Cannot advance: queue is empty.");

        QueueIndex = (QueueIndex + 1) % _queue.Count;
        TrackId = _queue[QueueIndex];
        PositionMs = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AdvancePrevious()
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("Cannot advance: queue is empty.");

        QueueIndex = (QueueIndex - 1 + _queue.Count) % _queue.Count;
        TrackId = _queue[QueueIndex];
        PositionMs = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Seek(long positionMs)
    {
        if (TrackId is null)
            throw new InvalidOperationException("Cannot seek: no track is loaded.");
        ArgumentOutOfRangeException.ThrowIfNegative(positionMs);

        PositionMs = positionMs;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClaimActiveIfNone(Guid deviceId)
    {
        if (ActiveDeviceId is null)
        {
            ActiveDeviceId = deviceId;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
