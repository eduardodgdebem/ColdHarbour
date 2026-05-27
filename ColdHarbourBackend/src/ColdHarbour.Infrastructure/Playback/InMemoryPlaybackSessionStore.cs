using System.Collections.Concurrent;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Infrastructure.Playback;

public sealed class InMemoryPlaybackSessionStore : IPlaybackSessionStore
{
    private readonly ConcurrentDictionary<Guid, PlaybackSession> _sessions = new();

    public PlaybackSession GetOrCreate(Guid userId) =>
        _sessions.GetOrAdd(userId, id => PlaybackSession.Create(id));

    public IReadOnlyList<PlaybackSession> GetAllForUser(Guid userId) =>
        _sessions.TryGetValue(userId, out var session) ? [session] : [];

    /// <summary>No-op: in-memory sessions are never persisted.</summary>
    public Task SaveAsync(PlaybackSession session, bool isHeartbeat, CancellationToken ct = default) =>
        Task.CompletedTask;
}
