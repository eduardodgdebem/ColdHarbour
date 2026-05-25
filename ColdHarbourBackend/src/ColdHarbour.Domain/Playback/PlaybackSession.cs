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

    public RepeatMode RepeatMode { get; private set; } = RepeatMode.Off;
    public bool Shuffle { get; private set; }

    // Stable shuffled visit order — indices into _queue. Re-seeded on
    // SetQueue or SetShuffle(true). _shuffleCursor advances through this
    // list during AdvanceAfterEnd; reaching the end ends the cycle.
    private List<int> _shuffleOrder = [];
    private int _shuffleCursor;

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
        _shuffleOrder.Clear();
        _shuffleCursor = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetQueue(IReadOnlyList<Guid> trackIds, int startIndex = 0, Random? rng = null)
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

        if (Shuffle) RebuildShuffleOrder(rng);
        else
        {
            _shuffleOrder.Clear();
            _shuffleCursor = 0;
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

        if (Shuffle && _shuffleOrder.Count > 0)
        {
            // User-clicked Next always advances + wraps (never stops at end of
            // cycle, unlike AdvanceAfterEnd which respects RepeatMode).
            _shuffleCursor = (_shuffleCursor + 1) % _shuffleOrder.Count;
            QueueIndex = _shuffleOrder[_shuffleCursor];
        }
        else
        {
            QueueIndex = (QueueIndex + 1) % _queue.Count;
        }

        TrackId = _queue[QueueIndex];
        PositionMs = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AdvancePrevious()
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("Cannot advance: queue is empty.");

        if (Shuffle && _shuffleOrder.Count > 0)
        {
            _shuffleCursor = (_shuffleCursor - 1 + _shuffleOrder.Count) % _shuffleOrder.Count;
            QueueIndex = _shuffleOrder[_shuffleCursor];
        }
        else
        {
            QueueIndex = (QueueIndex - 1 + _queue.Count) % _queue.Count;
        }

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

    public void SetRepeatMode(RepeatMode mode)
    {
        RepeatMode = mode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetShuffle(bool enabled, Random? rng = null)
    {
        Shuffle = enabled;
        if (enabled) RebuildShuffleOrder(rng);
        else
        {
            _shuffleOrder.Clear();
            _shuffleCursor = 0;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Called when the active device finishes playing the current track.
    /// Picks the next track based on <see cref="RepeatMode"/> and
    /// <see cref="Shuffle"/>. May stop playback (RepeatMode.Off at end of
    /// cycle) by clearing <see cref="TrackId"/> and <see cref="IsPlaying"/>.
    /// </summary>
    public void AdvanceAfterEnd(Random? rng = null)
    {
        // RepeatMode.One: restart the same track regardless of shuffle.
        if (RepeatMode == RepeatMode.One && TrackId is not null)
        {
            PositionMs = 0;
            IsPlaying = true;
            UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (_queue.Count == 0) return;

        if (Shuffle)
        {
            _shuffleCursor++;
            if (_shuffleCursor >= _shuffleOrder.Count)
            {
                if (RepeatMode == RepeatMode.All)
                {
                    RebuildShuffleOrder(rng);
                    _shuffleCursor = 0;
                }
                else
                {
                    StopAtEndOfCycle();
                    return;
                }
            }
            QueueIndex = _shuffleOrder[_shuffleCursor];
        }
        else
        {
            var next = QueueIndex + 1;
            if (next >= _queue.Count)
            {
                if (RepeatMode == RepeatMode.All)
                    next = 0;
                else
                {
                    StopAtEndOfCycle();
                    return;
                }
            }
            QueueIndex = next;
        }

        TrackId = _queue[QueueIndex];
        PositionMs = 0;
        IsPlaying = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void StopAtEndOfCycle()
    {
        IsPlaying = false;
        TrackId = null;
        PositionMs = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void RebuildShuffleOrder(Random? rng)
    {
        var r = rng ?? Random.Shared;
        _shuffleOrder = Enumerable.Range(0, _queue.Count).ToList();
        // Fisher-Yates shuffle.
        for (int i = _shuffleOrder.Count - 1; i > 0; i--)
        {
            int j = r.Next(i + 1);
            (_shuffleOrder[i], _shuffleOrder[j]) = (_shuffleOrder[j], _shuffleOrder[i]);
        }
        // Anchor the cursor at the current QueueIndex's position so the
        // currently-playing track is treated as already-visited.
        _shuffleCursor = _shuffleOrder.IndexOf(QueueIndex);
        if (_shuffleCursor < 0) _shuffleCursor = 0;
    }
}
