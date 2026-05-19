using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Playback;

public sealed class DeviceRepository(ColdHarbourDbContext db) : IDeviceRepository
{
    public Task<Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default)
        => db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);

    public async Task AddAsync(Device device, CancellationToken ct = default)
        => await db.Devices.AddAsync(device, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
