using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Playback;

public sealed class DeviceRepository(ColdHarbourDbContext db) : IDeviceRepository
{
    public Task<Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default)
        => db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);

    public Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct = default)
        => db.Devices.AnyAsync(d => d.Id == deviceId && d.UserId == userId, ct);

    public async Task<IReadOnlyList<Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await db.Devices.Where(d => d.UserId == userId).OrderByDescending(d => d.LastSeenAt).ToListAsync(ct);

    public async Task AddAsync(Device device, CancellationToken ct = default)
        => await db.Devices.AddAsync(device, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => db.Devices
            .Where(d => d.LastSeenAt < cutoff)
            .ExecuteDeleteAsync(ct);
}
