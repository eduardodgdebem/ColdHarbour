using System.Collections.Concurrent;
using ColdHarbour.Application.Playback.Ports;

namespace ColdHarbour.Infrastructure.Playback;

public sealed class InMemoryConnectedDeviceStore : IConnectedDeviceStore
{
    private readonly ConcurrentDictionary<Guid, byte> _connected = new();

    public void Add(Guid deviceId) => _connected.TryAdd(deviceId, 0);
    public void Remove(Guid deviceId) => _connected.TryRemove(deviceId, out _);
    public IReadOnlySet<Guid> GetConnected() => _connected.Keys.ToHashSet();
}
