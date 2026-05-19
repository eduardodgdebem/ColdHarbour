using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Playback;

public sealed class StartPlaybackCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private StartPlaybackCommandHandler CreateHandler() => new(_store, _events);

    [Fact]
    public async Task Handle_CreatesPlayEventAndStartsSession()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new StartPlaybackCommand(userId, deviceId, trackId), CancellationToken.None);

        session.IsPlaying.Should().BeTrue();
        session.ActiveDeviceId.Should().Be(deviceId);
        session.TrackId.Should().Be(trackId);
        await _events.Received(1).AddAsync(Arg.Is<PlayEvent>(e => e.UserId == userId && e.DeviceId == deviceId && e.TrackId == trackId), Arg.Any<CancellationToken>());
        await _events.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class UpdatePlaybackPositionCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();

    private UpdatePlaybackPositionCommandHandler CreateHandler() => new(_store);

    [Fact]
    public async Task Handle_UpdatesPosition()
    {
        var userId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.Start(Guid.NewGuid(), Guid.NewGuid());
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new UpdatePlaybackPositionCommand(userId, 45_000), CancellationToken.None);

        session.PositionMs.Should().Be(45_000);
    }
}

public sealed class TransferPlaybackCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();

    private TransferPlaybackCommandHandler CreateHandler() => new(_store);

    [Fact]
    public async Task Handle_TransfersActiveDevice()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var newDeviceId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.Start(deviceId, Guid.NewGuid());
        session.UpdatePosition(30_000);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new TransferPlaybackCommand(userId, newDeviceId, 30_000), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(newDeviceId);
        session.PositionMs.Should().Be(30_000);
        session.IsPlaying.Should().BeTrue();
    }
}

public sealed class ListDevicesQueryHandlerTests
{
    private readonly IDeviceRepository _repo = Substitute.For<IDeviceRepository>();

    private ListDevicesQueryHandler CreateHandler() => new(_repo);

    [Fact]
    public async Task Handle_ReturnsMappedDtos()
    {
        var userId = Guid.NewGuid();
        var device = Device.Register(Guid.NewGuid(), userId, "Chrome", "UA", ["mp3"], "opus-128");
        _repo.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Device> { device });

        var result = await CreateHandler().Handle(new ListDevicesQuery(userId), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(device.Id);
        result[0].Name.Should().Be("Chrome");
    }
}
