using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Ports;

public interface IPlayEventRepository
{
    Task AddAsync(PlayEvent playEvent, CancellationToken ct = default);
    Task<PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns all open events (EndedAt IS NULL) started before <paramref name="before"/>.
    /// Used exclusively by the orphan-backfill command.
    /// </summary>
    Task<IReadOnlyList<PlayEvent>> FindOrphanedAsync(DateTimeOffset before, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
