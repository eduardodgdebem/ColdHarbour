using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Playback;

public sealed class PlayEventRepository(ColdHarbourDbContext db) : IPlayEventRepository
{
    public async Task AddAsync(PlayEvent playEvent, CancellationToken ct = default) =>
        await db.PlayEvents.AddAsync(playEvent, ct);

    public Task<PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct = default) =>
        db.PlayEvents
          .Where(e => e.UserId == userId && e.EndedAt == null)
          .OrderByDescending(e => e.StartedAt)
          .FirstOrDefaultAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
