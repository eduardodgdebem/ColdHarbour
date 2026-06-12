namespace ColdHarbour.Domain.Playback;

public sealed class PlaybackSession
{
    /// <summary>
    /// Hard ceiling on queue length, enforced by the aggregate itself. This is the absolute
    /// backstop against unbounded growth (notably incremental <see cref="AddToQueue"/> floods,
    /// which the edge validator does not bound). The configurable edge cap
    /// (<c>COLDHARBOUR_WS_MAX_QUEUE_SIZE</c>) should stay ≤ this value; its default matches it.
    /// </summary>
    public const int MaxQueueSize = 1000;

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

    /// <summary>
    /// Monotonic counter incremented by the actor after every material mutation.
    /// Never touched by the aggregate itself — the actor is the sole increment site.
    /// </summary>
    public long Revision { get; private set; }

    // Stable shuffled visit order — indices into _queue. Re-seeded on
    // SetQueue or SetShuffle(true). _shuffleCursor advances through this
    // list during AdvanceAfterEnd; reaching the end ends the cycle.
    private List<int> _shuffleOrder = [];
    private int _shuffleCursor;

    private PlaybackSession() { }

    public static PlaybackSession Create(Guid userId) =>
        new() { UserId = userId, UpdatedAt = DateTimeOffset.UtcNow };

    /// <summary>
    /// Re-hydrate a <see cref="PlaybackSession"/> from persisted data (e.g. a
    /// Postgres snapshot). All fields are restored exactly as stored; the shuffle
    /// order is re-seeded from the current queue + <paramref name="queueIndex"/>
    /// because the order itself is not persisted.
    /// </summary>
    public static PlaybackSession Restore(
        Guid userId,
        Guid? activeDeviceId,
        Guid? trackId,
        long positionMs,
        bool isPlaying,
        IReadOnlyList<Guid> queue,
        int queueIndex,
        RepeatMode repeatMode,
        bool shuffle,
        DateTimeOffset updatedAt,
        long revision = 0)
    {
        var s = new PlaybackSession
        {
            UserId = userId,
            ActiveDeviceId = activeDeviceId,
            TrackId = trackId,
            PositionMs = positionMs,
            IsPlaying = isPlaying,
            QueueIndex = queueIndex,
            RepeatMode = repeatMode,
            Shuffle = shuffle,
            UpdatedAt = updatedAt,
            Revision = revision,
        };
        s._queue.AddRange(queue);
        if (shuffle && s._queue.Count > 0)
            s.RebuildShuffleOrder(null);
        return s;
    }

    /// <summary>
    /// Called exclusively by <see cref="ColdHarbour.Api.Playback.PlaybackUserActor"/>
    /// after every material mutation, keeping the increment site singular.
    /// </summary>
    public void IncrementRevision() => Revision++;

    public void Start(Guid deviceId, Guid trackId)
    {
        ActiveDeviceId = deviceId;
        TrackId = trackId;
        PositionMs = 0;
        IsPlaying = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The server-derived "live" position. While playing it interpolates the last stored
    /// <see cref="PositionMs"/> forward by wall-clock since <see cref="UpdatedAt"/>; while paused
    /// it is the frozen <see cref="PositionMs"/>. Pure read — gives REST/non-WS callers an accurate
    /// now-playing position between heartbeats without forcing them onto the socket.
    /// </summary>
    public long CurrentPositionMs(DateTimeOffset now)
    {
        if (!IsPlaying) return PositionMs;
        var elapsed = (long)(now - UpdatedAt).TotalMilliseconds;
        return PositionMs + Math.Max(0, elapsed); // clock skew must never rewind the position
    }

    /// <summary>
    /// Apply a heartbeat position with a sanity bound. The accepted range is
    /// <c>[PositionMs - 250, PositionMs + maxForwardDriftMs]</c> — a small back-tolerance for
    /// clock skew on the active device, and a forward grace covering the heartbeat interval.
    /// A value outside the range (rogue process, replayed packet, debugger skip) is dropped:
    /// nothing changes, <see cref="UpdatedAt"/> is not bumped, and <c>false</c> is returned.
    /// </summary>
    public bool RecordHeartbeat(long positionMs, int maxForwardDriftMs)
    {
        const long backToleranceMs = 250;
        if (positionMs < PositionMs - backToleranceMs || positionMs > PositionMs + maxForwardDriftMs)
            return false;

        PositionMs = positionMs;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
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

        if (trackIds.Count > MaxQueueSize)
            throw new QueueTooLargeException(trackIds.Count, MaxQueueSize);

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

    /// <summary>
    /// The single place the "claim active when none, then apply the transport mutation" rule
    /// lives. Command handlers call this instead of duplicating
    /// <see cref="ClaimActiveIfNone"/> + the per-command mutation. When <paramref name="senderDeviceId"/>
    /// is null (e.g. a pause/resume that targets the existing active device), no claim happens.
    /// </summary>
    public void ApplyTransport(Guid? senderDeviceId, Action mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        if (senderDeviceId is { } deviceId)
            ClaimActiveIfNone(deviceId);
        mutate();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Releases ownership of the session when the active device has gone away for good
    /// (socket closed and <c>LastSeenAt</c> past the liveness TTL). Queue, track, position
    /// and <c>IsPlaying</c> are deliberately left intact so the next device to act can
    /// <see cref="ClaimActiveIfNone"/> and resume exactly where playback was. No-op when
    /// no device currently owns the session.
    /// </summary>
    public void DemoteActiveDevice()
    {
        if (ActiveDeviceId is null) return;
        ActiveDeviceId = null;
        UpdatedAt = DateTimeOffset.UtcNow;
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
    /// Insert <paramref name="trackId"/> into the queue at
    /// <paramref name="position"/> (clamped to <c>[0, Queue.Count]</c>).
    /// Adjusts <see cref="QueueIndex"/> so the currently-playing track
    /// stays the currently-playing track. On an empty queue, also primes
    /// playback (mirrors <see cref="SetQueue"/>).
    /// </summary>
    public void AddToQueue(Guid trackId, int? position = null)
    {
        if (_queue.Count >= MaxQueueSize)
            throw new QueueTooLargeException(_queue.Count + 1, MaxQueueSize);

        var insertAt = position is null
            ? _queue.Count
            : Math.Clamp(position.Value, 0, _queue.Count);

        if (_queue.Count == 0)
        {
            _queue.Add(trackId);
            QueueIndex = 0;
            TrackId = trackId;
            IsPlaying = true;
            PositionMs = 0;
            if (Shuffle) RebuildShuffleOrder(null);
            UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        _queue.Insert(insertAt, trackId);
        if (insertAt <= QueueIndex) QueueIndex++;
        if (Shuffle) RebuildShuffleOrder(null);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Remove the item at <paramref name="index"/>. Preserves the
    /// QueueIndex invariant:
    ///   index &lt; QueueIndex → QueueIndex decrements (current track unchanged);
    ///   index &gt; QueueIndex → no change to QueueIndex;
    ///   index == QueueIndex → advance to the next item (wraps when at end);
    ///     if that was the last item, clear TrackId and stop playback.
    /// </summary>
    public void RemoveFromQueue(int index)
    {
        if ((uint)index >= (uint)_queue.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is outside the queue range [0, {_queue.Count - 1}].");

        _queue.RemoveAt(index);

        if (_queue.Count == 0)
        {
            QueueIndex = 0;
            TrackId = null;
            IsPlaying = false;
            PositionMs = 0;
            _shuffleOrder.Clear();
            _shuffleCursor = 0;
            UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (index < QueueIndex)
        {
            QueueIndex--;
        }
        else if (index == QueueIndex)
        {
            // The currently-playing item was removed. Stay at the same
            // logical position in the queue; if we were at the end, wrap.
            if (QueueIndex >= _queue.Count) QueueIndex = 0;
            TrackId = _queue[QueueIndex];
            PositionMs = 0;
        }
        // index > QueueIndex: nothing to do.

        if (Shuffle) RebuildShuffleOrder(null);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Move the item at <paramref name="from"/> to <paramref name="to"/>.
    /// <see cref="QueueIndex"/> follows the originally-current item so it
    /// keeps pointing at the same track after the reorder.
    /// </summary>
    public void ReorderQueue(int from, int to)
    {
        if ((uint)from >= (uint)_queue.Count)
            throw new ArgumentOutOfRangeException(nameof(from), $"From index {from} is outside the queue range [0, {_queue.Count - 1}].");
        if ((uint)to >= (uint)_queue.Count)
            throw new ArgumentOutOfRangeException(nameof(to), $"To index {to} is outside the queue range [0, {_queue.Count - 1}].");
        if (from == to)
        {
            UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        var currentTrackId = TrackId;
        var item = _queue[from];
        _queue.RemoveAt(from);
        _queue.Insert(to, item);

        // Recompute QueueIndex by locating the originally-current track.
        if (currentTrackId is not null)
        {
            var newIdx = _queue.IndexOf(currentTrackId.Value);
            if (newIdx >= 0) QueueIndex = newIdx;
        }

        if (Shuffle) RebuildShuffleOrder(null);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Drop every queued item and stop playback. Unlike <see cref="Clear"/>,
    /// this preserves <see cref="ActiveDeviceId"/> — the active device
    /// stays subscribed and ready for a fresh SetQueue.
    /// </summary>
    public void ClearQueue()
    {
        _queue.Clear();
        QueueIndex = 0;
        TrackId = null;
        IsPlaying = false;
        PositionMs = 0;
        _shuffleOrder.Clear();
        _shuffleCursor = 0;
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
