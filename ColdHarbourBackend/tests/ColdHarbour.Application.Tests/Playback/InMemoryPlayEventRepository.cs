using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Tests.Playback;

/// <summary>
/// Test-only in-memory implementation. Stores events in a plain list so
/// lifecycle invariant tests can count open / closed rows without Testcontainers.
/// </summary>
internal sealed class InMemoryPlayEventRepository : IPlayEventRepository
{
    private readonly List<PlayEvent> _events = [];

    public Task AddAsync(PlayEvent playEvent, CancellationToken ct = default)
    {
        _events.Add(playEvent);
        return Task.CompletedTask;
    }

    public Task<PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var active = _events
            .Where(e => e.UserId == userId && e.EndedAt is null)
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefault();
        return Task.FromResult(active);
    }

    public Task<IReadOnlyList<PlayEvent>> FindOrphanedAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        IReadOnlyList<PlayEvent> result = _events
            .Where(e => e.EndedAt is null && e.StartedAt < before)
            .ToList();
        return Task.FromResult(result);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public IReadOnlyList<PlayEvent> GetAll() => _events.AsReadOnly();

    public int CountOpenByUser(Guid userId) =>
        _events.Count(e => e.UserId == userId && e.EndedAt is null);

    public int CountClosedByUser(Guid userId) =>
        _events.Count(e => e.UserId == userId && e.EndedAt is not null);

    public int TotalByUser(Guid userId) =>
        _events.Count(e => e.UserId == userId);
}
