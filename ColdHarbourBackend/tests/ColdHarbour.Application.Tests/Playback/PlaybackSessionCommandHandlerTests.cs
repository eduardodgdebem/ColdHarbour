using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Playback;

// Session is now passed directly into commands — store is no longer injected into handlers.
// Each test creates the session, passes it into the command, and asserts on its state.

public sealed class UpdatePlaybackPositionCommandHandlerTests
{
    [Fact]
    public async Task Handle_UpdatesPosition()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid() }, 0);

        var changed = await new UpdatePlaybackPositionCommandHandler()
            .Handle(new UpdatePlaybackPositionCommand(session, 45_000), CancellationToken.None);

        session.PositionMs.Should().Be(45_000);
        changed.Should().BeTrue();
    }
}

public sealed class TransferPlaybackCommandHandlerTests
{
    [Fact]
    public async Task Handle_TransfersActiveDevice()
    {
        var deviceId = Guid.NewGuid();
        var newDeviceId = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid() }, 0);
        session.ClaimActiveIfNone(deviceId);
        session.UpdatePosition(30_000);

        var changed = await new TransferPlaybackCommandHandler()
            .Handle(new TransferPlaybackCommand(session, newDeviceId, 30_000), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(newDeviceId);
        session.PositionMs.Should().Be(30_000);
        session.IsPlaying.Should().BeTrue();
        changed.Should().BeTrue();
    }
}

public sealed class SetQueueCommandHandlerTests
{
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private SetQueueCommandHandler CreateHandler() => new(_events);

    [Fact]
    public async Task Handle_SetsQueueStartsPlaybackAndRecordsPlayEvent()
    {
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var changed = await CreateHandler().Handle(new SetQueueCommand(session, tracks, 2, sender), CancellationToken.None);

        session.Queue.Should().Equal(tracks);
        session.QueueIndex.Should().Be(2);
        session.TrackId.Should().Be(tracks[2]);
        session.IsPlaying.Should().BeTrue();
        session.ActiveDeviceId.Should().Be(sender);
        changed.Should().BeTrue();

        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.UserId == session.UserId && e.DeviceId == sender && e.TrackId == tracks[2]),
            Arg.Any<CancellationToken>());
        await _events.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeepsExistingActiveDeviceWhenAnotherIsAlreadyPlaying()
    {
        var existingActive = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid() }, 0);
        session.ClaimActiveIfNone(existingActive);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await CreateHandler().Handle(new SetQueueCommand(session, tracks, 0, sender), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(existingActive);
    }

    [Fact]
    public async Task Handle_EmptyTracks_DoesNotRecordPlayEvent()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());

        var changed = await CreateHandler().Handle(new SetQueueCommand(session, Array.Empty<Guid>(), 0, Guid.NewGuid()), CancellationToken.None);

        session.Queue.Should().BeEmpty();
        changed.Should().BeFalse();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }
}

public sealed class NextTrackCommandHandlerTests
{
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private NextTrackCommandHandler CreateHandler() => new(_events);

    [Fact]
    public async Task Handle_AdvancesQueueAndRecordsPlayEvent()
    {
        var active = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(active);

        var changed = await CreateHandler().Handle(new NextTrackCommand(session, Guid.NewGuid()), CancellationToken.None);

        session.QueueIndex.Should().Be(1);
        session.TrackId.Should().Be(tracks[1]);
        changed.Should().BeTrue();
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == tracks[1] && e.DeviceId == active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SenderClaimsActiveWhenSessionHasNone()
    {
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 0);

        await CreateHandler().Handle(new NextTrackCommand(session, sender), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(sender);
    }

    [Fact]
    public async Task Handle_EmptyQueue_NoOp()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());

        var changed = await CreateHandler().Handle(new NextTrackCommand(session, Guid.NewGuid()), CancellationToken.None);

        session.TrackId.Should().BeNull();
        changed.Should().BeFalse();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }
}

public sealed class PreviousTrackCommandHandlerTests
{
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private PreviousTrackCommandHandler CreateHandler() => new(_events);

    [Fact]
    public async Task Handle_MovesIndexBackAndRecordsPlayEvent()
    {
        var active = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 1);
        session.ClaimActiveIfNone(active);

        var changed = await CreateHandler().Handle(new PreviousTrackCommand(session, Guid.NewGuid()), CancellationToken.None);

        session.QueueIndex.Should().Be(0);
        session.TrackId.Should().Be(tracks[0]);
        changed.Should().BeTrue();
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == tracks[0] && e.DeviceId == active),
            Arg.Any<CancellationToken>());
    }
}

public sealed class SeekCommandHandlerTests
{
    [Fact]
    public async Task Handle_UpdatesPositionAndClaimsActiveIfNone()
    {
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid() }, 0);

        var changed = await new SeekCommandHandler()
            .Handle(new SeekCommand(session, sender, 12_345), CancellationToken.None);

        session.PositionMs.Should().Be(12_345);
        session.ActiveDeviceId.Should().Be(sender);
        changed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoTrackLoaded_NoOp()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());

        var changed = await new SeekCommandHandler()
            .Handle(new SeekCommand(session, Guid.NewGuid(), 5_000), CancellationToken.None);

        session.PositionMs.Should().Be(0);
        changed.Should().BeFalse();
    }
}

public sealed class SetRepeatModeCommandHandlerTests
{
    [Fact]
    public async Task Handle_StoresRepeatMode()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());

        var changed = await new SetRepeatModeCommandHandler()
            .Handle(new SetRepeatModeCommand(session, RepeatMode.All), CancellationToken.None);

        session.RepeatMode.Should().Be(RepeatMode.All);
        changed.Should().BeTrue();
    }
}

public sealed class SetShuffleCommandHandlerTests
{
    [Fact]
    public async Task Handle_StoresShuffleFlag()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 0);

        var changed = await new SetShuffleCommandHandler()
            .Handle(new SetShuffleCommand(session, true), CancellationToken.None);

        session.Shuffle.Should().BeTrue();
        changed.Should().BeTrue();
    }
}

public sealed class TrackEndedCommandHandlerTests
{
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private TrackEndedCommandHandler CreateHandler() => new(_events);

    [Fact]
    public async Task Handle_ClosesActivePlayEventAndAdvancesQueue()
    {
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(device);

        var openEvent = PlayEvent.Begin(session.UserId, device, tracks[0]);
        _events.FindActiveByUserAsync(session.UserId, Arg.Any<CancellationToken>()).Returns(openEvent);

        var changed = await CreateHandler().Handle(
            new TrackEndedCommand(session, device, tracks[0], 180_000),
            CancellationToken.None);

        openEvent.EndedAt.Should().NotBeNull();
        openEvent.CompletedRatio.Should().Be(1.0);
        session.TrackId.Should().Be(tracks[1]);
        changed.Should().BeTrue();
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == tracks[1] && e.DeviceId == device),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatOne_RestartsSameTrack_NoNewPlayEvent()
    {
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { track }, 0);
        session.ClaimActiveIfNone(device);
        session.SetRepeatMode(RepeatMode.One);

        var openEvent = PlayEvent.Begin(session.UserId, device, track);
        _events.FindActiveByUserAsync(session.UserId, Arg.Any<CancellationToken>()).Returns(openEvent);

        var changed = await CreateHandler().Handle(
            new TrackEndedCommand(session, device, track, 180_000),
            CancellationToken.None);

        openEvent.EndedAt.Should().NotBeNull();
        session.TrackId.Should().Be(track);
        session.PositionMs.Should().Be(0);
        changed.Should().BeTrue();
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == track),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatOff_LastTrack_StopsAndNoNewPlayEvent()
    {
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { track }, 0);
        session.ClaimActiveIfNone(device);

        var openEvent = PlayEvent.Begin(session.UserId, device, track);
        _events.FindActiveByUserAsync(session.UserId, Arg.Any<CancellationToken>()).Returns(openEvent);

        var changed = await CreateHandler().Handle(
            new TrackEndedCommand(session, device, track, 180_000),
            CancellationToken.None);

        session.TrackId.Should().BeNull();
        session.IsPlaying.Should().BeFalse();
        changed.Should().BeTrue();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FromNonActiveDevice_NoOp()
    {
        var activeDevice = Guid.NewGuid();
        var staleDevice = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(activeDevice);

        var changed = await CreateHandler().Handle(
            new TrackEndedCommand(session, staleDevice, tracks[0], 180_000),
            CancellationToken.None);

        session.QueueIndex.Should().Be(0);
        session.TrackId.Should().Be(tracks[0]);
        changed.Should().BeFalse();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }
}

public sealed class AddToQueueCommandHandlerTests
{
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private AddToQueueCommandHandler CreateHandler() => new(_events);

    [Fact]
    public async Task Handle_AppendsAndClaimsActiveIfNone()
    {
        var sender = Guid.NewGuid();
        var existing = new[] { Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(existing, 0);
        var newTrack = Guid.NewGuid();

        var changed = await CreateHandler().Handle(
            new AddToQueueCommand(session, sender, newTrack, null),
            CancellationToken.None);

        session.Queue.Should().Equal(existing[0], newTrack);
        session.ActiveDeviceId.Should().Be(sender);
        changed.Should().BeTrue();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FirstAddToEmptyQueue_RecordsPlayEvent()
    {
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        var t = Guid.NewGuid();

        var changed = await CreateHandler().Handle(
            new AddToQueueCommand(session, sender, t, null),
            CancellationToken.None);

        session.TrackId.Should().Be(t);
        session.IsPlaying.Should().BeTrue();
        changed.Should().BeTrue();
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == t && e.DeviceId == sender),
            Arg.Any<CancellationToken>());
    }
}

public sealed class RemoveFromQueueCommandHandlerTests
{
    private readonly IPlayEventRepository _events = Substitute.For<IPlayEventRepository>();

    private RemoveFromQueueCommandHandler CreateHandler() => new(_events);

    [Fact]
    public async Task Handle_RemovingCurrentTrack_ClosesOldEventAndOpensNew()
    {
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(device);

        var openEvent = PlayEvent.Begin(session.UserId, device, tracks[0]);
        _events.FindActiveByUserAsync(session.UserId, Arg.Any<CancellationToken>()).Returns(openEvent);

        var changed = await CreateHandler().Handle(
            new RemoveFromQueueCommand(session, device, 0),
            CancellationToken.None);

        session.Queue.Should().ContainSingle().Which.Should().Be(tracks[1]);
        session.TrackId.Should().Be(tracks[1]);
        openEvent.EndedAt.Should().NotBeNull();
        changed.Should().BeTrue();
        await _events.Received(1).AddAsync(
            Arg.Is<PlayEvent>(e => e.TrackId == tracks[1] && e.DeviceId == device),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemovingNonCurrentTrack_DoesNotTouchPlayEvents()
    {
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(device);

        var changed = await CreateHandler().Handle(
            new RemoveFromQueueCommand(session, device, 2),
            CancellationToken.None);

        session.Queue.Count.Should().Be(2);
        session.TrackId.Should().Be(tracks[0]);
        changed.Should().BeTrue();
        await _events.DidNotReceive().AddAsync(Arg.Any<PlayEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OutOfRangeIndex_NoOp()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid() }, 0);

        var changed = await CreateHandler().Handle(
            new RemoveFromQueueCommand(session, Guid.NewGuid(), 99),
            CancellationToken.None);

        session.Queue.Count.Should().Be(1);
        changed.Should().BeFalse();
    }
}

public sealed class ReorderQueueCommandHandlerTests
{
    [Fact]
    public async Task Handle_MovesItem_KeepingCurrentTrackPlaying()
    {
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);

        var changed = await new ReorderQueueCommandHandler().Handle(
            new ReorderQueueCommand(session, Guid.NewGuid(), 0, 2),
            CancellationToken.None);

        session.Queue.Should().Equal(tracks[1], tracks[2], tracks[0]);
        session.TrackId.Should().Be(tracks[0]);
        changed.Should().BeTrue();
    }
}

public sealed class ClearQueueCommandHandlerTests
{
    [Fact]
    public async Task Handle_ClearsQueueAndStopsPlayback()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 1);

        var changed = await new ClearQueueCommandHandler().Handle(
            new ClearQueueCommand(session, Guid.NewGuid()),
            CancellationToken.None);

        session.Queue.Should().BeEmpty();
        session.IsPlaying.Should().BeFalse();
        changed.Should().BeTrue();
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
