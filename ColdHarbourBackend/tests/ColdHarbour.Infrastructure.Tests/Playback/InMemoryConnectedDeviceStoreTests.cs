using ColdHarbour.Infrastructure.Playback;
using FluentAssertions;

namespace ColdHarbour.Infrastructure.Tests.Playback;

public sealed class InMemoryConnectedDeviceStoreTests
{
    private readonly InMemoryConnectedDeviceStore _sut = new();

    [Fact]
    public void Add_ThenGetConnected_ContainsDevice()
    {
        var deviceId = Guid.NewGuid();

        _sut.Add(deviceId);

        _sut.GetConnected().Should().Contain(deviceId);
    }

    [Fact]
    public void Remove_AfterAdd_DeviceNotInConnected()
    {
        var deviceId = Guid.NewGuid();
        _sut.Add(deviceId);

        _sut.Remove(deviceId);

        _sut.GetConnected().Should().NotContain(deviceId);
    }

    [Fact]
    public void GetConnected_IsSnapshotNotLiveView()
    {
        var deviceId1 = Guid.NewGuid();
        var deviceId2 = Guid.NewGuid();
        _sut.Add(deviceId1);

        var snapshot = _sut.GetConnected();

        _sut.Add(deviceId2);

        snapshot.Should().NotContain(deviceId2, "GetConnected returns a snapshot; mutations after the call must not affect it");
    }
}
