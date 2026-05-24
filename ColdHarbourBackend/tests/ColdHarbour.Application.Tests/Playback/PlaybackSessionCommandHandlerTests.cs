using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Playback;

public sealed class UpdatePlaybackPositionCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();

    private UpdatePlaybackPositionCommandHandler CreateHandler() => new(_store);

    [Fact]
    public async Task Handle_UpdatesPosition()
    {
        var userId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.SetQueue(new[] { Guid.NewGuid() }, 0);
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
        session.SetQueue(new[] { Guid.NewGuid() }, 0);
        session.ClaimActiveIfNone(deviceId);
        session.UpdatePosition(30_000);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new TransferPlaybackCommand(userId, newDeviceId, 30_000), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(newDeviceId);
        session.PositionMs.Should().Be(30_000);
        session.IsPlaying.Should().BeTrue();
    }
}

public sealed class SetQueueCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private SetQueueCommandHandler CreateHandler() => new(_store, _events);

    [Fact]
    public async Task Handle_SetsQueueStartsPlaybackAndRecordsPlayEvent()
    {
        var userId = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        _store.GetOrCreate(userId).Returns(session);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await CreateHandler().Handle(new SetQueueCommand(userId, tracks, 2, sender), CancellationToken.None);

        session.Queue.Should().Equal(tracks);
        session.QueueIndex.Should().Be(2);
        session.TrackId.Should().Be(tracks[2]);
        session.IsPlaying.Should().BeTrue();
        session.ActiveDeviceId.Should().Be(sender);

        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.UserId == userId && e.DeviceId == sender && e.TrackId == tracks[2]),
            Arg.Any<CancellationToken>());
        await _events.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeepsExistingActiveDeviceWhenAnotherIsAlreadyPlaying()
    {
        var userId = Guid.NewGuid();
        var existingActive = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.SetQueue(new[] { Guid.NewGuid() }, 0);
        session.ClaimActiveIfNone(existingActive);
        _store.GetOrCreate(userId).Returns(session);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await CreateHandler().Handle(new SetQueueCommand(userId, tracks, 0, sender), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(existingActive);
    }

    [Fact]
    public async Task Handle_EmptyTracks_DoesNotRecordPlayEvent()
    {
        var userId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new SetQueueCommand(userId, Array.Empty<Guid>(), 0, Guid.NewGuid()), CancellationToken.None);

        session.Queue.Should().BeEmpty();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }
}

public sealed class NextTrackCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private NextTrackCommandHandler CreateHandler() => new(_store, _events);

    [Fact]
    public async Task Handle_AdvancesQueueAndRecordsPlayEvent()
    {
        var userId = Guid.NewGuid();
        var active = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(active);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new NextTrackCommand(userId, Guid.NewGuid()), CancellationToken.None);

        session.QueueIndex.Should().Be(1);
        session.TrackId.Should().Be(tracks[1]);
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == tracks[1] && e.DeviceId == active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SenderClaimsActiveWhenSessionHasNone()
    {
        var userId = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 0);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new NextTrackCommand(userId, sender), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(sender);
    }

    [Fact]
    public async Task Handle_EmptyQueue_NoOp()
    {
        var userId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new NextTrackCommand(userId, Guid.NewGuid()), CancellationToken.None);

        session.TrackId.Should().BeNull();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }
}

public sealed class PreviousTrackCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private PreviousTrackCommandHandler CreateHandler() => new(_store, _events);

    [Fact]
    public async Task Handle_MovesIndexBackAndRecordsPlayEvent()
    {
        var userId = Guid.NewGuid();
        var active = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 1);
        session.ClaimActiveIfNone(active);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new PreviousTrackCommand(userId, Guid.NewGuid()), CancellationToken.None);

        session.QueueIndex.Should().Be(0);
        session.TrackId.Should().Be(tracks[0]);
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == tracks[0] && e.DeviceId == active),
            Arg.Any<CancellationToken>());
    }
}

public sealed class SeekCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();

    private SeekCommandHandler CreateHandler() => new(_store);

    [Fact]
    public async Task Handle_UpdatesPositionAndClaimsActiveIfNone()
    {
        var userId = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.SetQueue(new[] { Guid.NewGuid() }, 0);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new SeekCommand(userId, sender, 12_345), CancellationToken.None);

        session.PositionMs.Should().Be(12_345);
        session.ActiveDeviceId.Should().Be(sender);
    }

    [Fact]
    public async Task Handle_NoTrackLoaded_NoOp()
    {
        var userId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(new SeekCommand(userId, Guid.NewGuid(), 5_000), CancellationToken.None);

        session.PositionMs.Should().Be(0);
    }
}

public sealed class SetRepeatModeCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();

    [Fact]
    public async Task Handle_StoresRepeatMode()
    {
        var userId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        _store.GetOrCreate(userId).Returns(session);

        await new SetRepeatModeCommandHandler(_store)
            .Handle(new SetRepeatModeCommand(userId, RepeatMode.All), CancellationToken.None);

        session.RepeatMode.Should().Be(RepeatMode.All);
    }
}

public sealed class SetShuffleCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();

    [Fact]
    public async Task Handle_StoresShuffleFlag()
    {
        var userId = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 0);
        _store.GetOrCreate(userId).Returns(session);

        await new SetShuffleCommandHandler(_store)
            .Handle(new SetShuffleCommand(userId, true), CancellationToken.None);

        session.Shuffle.Should().BeTrue();
    }
}

public sealed class TrackEndedCommandHandlerTests
{
    private readonly IPlaybackSessionStore _store = Substitute.For<IPlaybackSessionStore>();
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private TrackEndedCommandHandler CreateHandler() => new(_store, _events);

    [Fact]
    public async Task Handle_ClosesActivePlayEventAndAdvancesQueue()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(device);
        _store.GetOrCreate(userId).Returns(session);

        var openEvent = PlayEvent.Begin(userId, device, tracks[0]);
        _events.FindActiveByUserAsync(userId, Arg.Any<CancellationToken>()).Returns(openEvent);

        await CreateHandler().Handle(
            new TrackEndedCommand(userId, device, tracks[0], 180_000),
            CancellationToken.None);

        openEvent.EndedAt.Should().NotBeNull();
        openEvent.CompletedRatio.Should().Be(1.0);
        session.TrackId.Should().Be(tracks[1]);
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == tracks[1] && e.DeviceId == device),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatOne_RestartsSameTrack_NoNewPlayEvent()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.SetQueue(new[] { track }, 0);
        session.ClaimActiveIfNone(device);
        session.SetRepeatMode(RepeatMode.One);
        _store.GetOrCreate(userId).Returns(session);

        var openEvent = PlayEvent.Begin(userId, device, track);
        _events.FindActiveByUserAsync(userId, Arg.Any<CancellationToken>()).Returns(openEvent);

        await CreateHandler().Handle(
            new TrackEndedCommand(userId, device, track, 180_000),
            CancellationToken.None);

        openEvent.EndedAt.Should().NotBeNull();
        session.TrackId.Should().Be(track);
        session.PositionMs.Should().Be(0);
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == track),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatOff_LastTrack_StopsAndNoNewPlayEvent()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.SetQueue(new[] { track }, 0);
        session.ClaimActiveIfNone(device);
        _store.GetOrCreate(userId).Returns(session);

        var openEvent = PlayEvent.Begin(userId, device, track);
        _events.FindActiveByUserAsync(userId, Arg.Any<CancellationToken>()).Returns(openEvent);

        await CreateHandler().Handle(
            new TrackEndedCommand(userId, device, track, 180_000),
            CancellationToken.None);

        session.TrackId.Should().BeNull();
        session.IsPlaying.Should().BeFalse();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FromNonActiveDevice_NoOp()
    {
        var userId = Guid.NewGuid();
        var activeDevice = Guid.NewGuid();
        var staleDevice = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(activeDevice);
        _store.GetOrCreate(userId).Returns(session);

        await CreateHandler().Handle(
            new TrackEndedCommand(userId, staleDevice, tracks[0], 180_000),
            CancellationToken.None);

        session.QueueIndex.Should().Be(0);
        session.TrackId.Should().Be(tracks[0]);
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }
}

public sealed class ListDevicesQueryHandlerTests
{
    private readonly IDeviceRepository _repo = Substitute.For<IDeviceRepository>();
    private readonly IConnectedDeviceStore _connected = Substitute.For<IConnectedDeviceStore>();

    private ListDevicesQueryHandler CreateHandler() => new(_repo, _connected);

    [Fact]
    public async Task Handle_ReturnsMappedDtos()
    {
        var userId = Guid.NewGuid();
        var device = Device.Register(Guid.NewGuid(), userId, "Chrome", "UA", ["mp3"], "opus-128");
        _repo.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Device> { device });
        _connected.GetConnected().Returns((IReadOnlySet<Guid>)new HashSet<Guid> { device.Id });

        var result = await CreateHandler().Handle(new ListDevicesQuery(userId), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(device.Id);
        result[0].Name.Should().Be("Chrome");
    }
}
