using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Ports;

public interface IDeviceRepository
{
    Task<Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default);

    /// <summary>
    /// True if <paramref name="deviceId"/> is a registered device owned by
    /// <paramref name="userId"/>. Single-row PK lookup. Used to reject WS commands whose
    /// sender device the client invented (claim-active spoofing).
    /// </summary>
    Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct = default);

    Task<IReadOnlyList<Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Device device, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
