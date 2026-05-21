namespace ColdHarbour.Application.Playback.Ports;

public interface IConnectedDeviceStore
{
    void Add(Guid deviceId);
    void Remove(Guid deviceId);
    IReadOnlySet<Guid> GetConnected();
}
