namespace ColdHarbour.Domain.Playback;

/// <summary>
/// Thrown when a queue mutation would push the queue past
/// <see cref="PlaybackSession.MaxQueueSize"/>. The whole queue is JSON-serialized into the
/// snapshot store and broadcast to every subscriber on each change, so an unbounded queue is a
/// memory/bandwidth hazard. A cap turns a runaway client into a benign rejection.
/// </summary>
public sealed class QueueTooLargeException(int attempted, int limit)
    : Exception($"Queue would hold {attempted} tracks, exceeding the limit of {limit}.")
{
    public int Attempted { get; } = attempted;
    public int Limit { get; } = limit;
}
