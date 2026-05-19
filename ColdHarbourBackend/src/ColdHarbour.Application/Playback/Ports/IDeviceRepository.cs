using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Ports;

public interface IDeviceRepository
{
    Task<Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Device device, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
