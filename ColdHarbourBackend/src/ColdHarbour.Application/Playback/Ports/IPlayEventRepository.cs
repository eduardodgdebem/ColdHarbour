using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Ports;

public interface IPlayEventRepository
{
    Task AddAsync(PlayEvent playEvent, CancellationToken ct = default);
    Task<PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
