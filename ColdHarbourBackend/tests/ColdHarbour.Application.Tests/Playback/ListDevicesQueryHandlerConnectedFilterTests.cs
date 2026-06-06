using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback;

public sealed class ListDevicesQueryHandlerConnectedFilterTests
{
    // --- stubs ---

    private sealed class StubDeviceRepository : IDeviceRepository
    {
        private readonly List<Device> _devices;

        public StubDeviceRepository(params Device[] devices) => _devices = [.. devices];

        public Task<Device?> FindByIdAsync(Guid deviceId, CancellationToken ct = default)
            => Task.FromResult(_devices.FirstOrDefault(d => d.Id == deviceId));

        public Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct = default)
            => Task.FromResult(_devices.Any(d => d.Id == deviceId && d.UserId == userId));

        public Task<IReadOnlyList<Device>> ListByUserIdAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Device>>(_devices.Where(d => d.UserId == userId).ToList());

        public Task AddAsync(Device device, CancellationToken ct = default)
        {
            _devices.Add(device);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubConnectedDeviceStore : IConnectedDeviceStore
    {
        private readonly HashSet<Guid> _connected;

        public StubConnectedDeviceStore(params Guid[] connectedIds) => _connected = [.. connectedIds];

        public void Add(Guid deviceId) => _connected.Add(deviceId);
        public void Remove(Guid deviceId) => _connected.Remove(deviceId);
        public IReadOnlySet<Guid> GetConnected() => _connected;
    }

    private static ListDevicesQueryHandler CreateHandler(IDeviceRepository repo, IConnectedDeviceStore store)
        => new(repo, store);

    // --- tests ---

    [Fact]
    public async Task ListDevices_WhenNoDevicesConnected_ReturnsEmpty()
    {
        var userId = Guid.NewGuid();
        var device = Device.Register(Guid.NewGuid(), userId, "Chrome", "UA", ["mp3"], "opus-128");
        var repo = new StubDeviceRepository(device);
        var store = new StubConnectedDeviceStore();

        var result = await CreateHandler(repo, store)
            .Handle(new ListDevicesQuery(userId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListDevices_WhenSomeDevicesConnected_ReturnsOnlyConnected()
    {
        var userId = Guid.NewGuid();
        var deviceA = Device.Register(Guid.NewGuid(), userId, "Chrome", "UA", ["mp3"], "opus-128");
        var deviceB = Device.Register(Guid.NewGuid(), userId, "Safari", "UA2", ["aac"], "aac-192");
        var repo = new StubDeviceRepository(deviceA, deviceB);
        var store = new StubConnectedDeviceStore(deviceA.Id);

        var result = await CreateHandler(repo, store)
            .Handle(new ListDevicesQuery(userId), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(deviceA.Id);
    }

    [Fact]
    public async Task ListDevices_WhenAllDevicesConnected_ReturnsAll()
    {
        var userId = Guid.NewGuid();
        var deviceA = Device.Register(Guid.NewGuid(), userId, "Chrome", "UA", ["mp3"], "opus-128");
        var deviceB = Device.Register(Guid.NewGuid(), userId, "Safari", "UA2", ["aac"], "aac-192");
        var repo = new StubDeviceRepository(deviceA, deviceB);
        var store = new StubConnectedDeviceStore(deviceA.Id, deviceB.Id);

        var result = await CreateHandler(repo, store)
            .Handle(new ListDevicesQuery(userId), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(d => d.Id).Should().BeEquivalentTo([deviceA.Id, deviceB.Id]);
    }
}
