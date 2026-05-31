using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Playback;

// Session is passed directly into commands — store is no longer injected into handlers.
// Event lifecycle is delegated to IPlaySessionTimeline (verified by PlaySessionTimelineTests and
// PlayEventLifecycleTests). These tests verify session-state mutations and timeline call contracts.

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
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    [Fact]
    public async Task Handle_TransfersActiveDeviceAndCallsTimeline()
    {
        var deviceId = Guid.NewGuid();
        var newDeviceId = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { Guid.NewGuid() }, 0);
        session.ClaimActiveIfNone(deviceId);
        session.UpdatePosition(30_000);

        var changed = await new TransferPlaybackCommandHandler(_timeline)
            .Handle(new TransferPlaybackCommand(session, newDeviceId, 30_000), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(newDeviceId);
        session.PositionMs.Should().Be(30_000);
        session.IsPlaying.Should().BeTrue();
        changed.Should().BeTrue();

        await _timeline.Received(1).ActiveDeviceChangedAsync(
            session.UserId, deviceId, 30_000, newDeviceId, Arg.Any<CancellationToken>());
    }
}

public sealed class SetQueueCommandHandlerTests
{
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    private SetQueueCommandHandler CreateHandler() => new(_timeline);

    [Fact]
    public async Task Handle_SetsQueueStartsPlaybackAndCallsTimeline()
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

        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, sender, null, 0, tracks[2], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeepsExistingActiveDeviceWhenAnotherIsAlreadyPlaying()
    {
        var existingActive = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        var initialTrack = Guid.NewGuid();
        session.SetQueue(new[] { initialTrack }, 0);
        session.ClaimActiveIfNone(existingActive);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await CreateHandler().Handle(new SetQueueCommand(session, tracks, 0, sender), CancellationToken.None);

        session.ActiveDeviceId.Should().Be(existingActive);
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, existingActive, initialTrack, 0, tracks[0], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyTracks_DoesNotCallTimeline()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());

        var changed = await CreateHandler().Handle(
            new SetQueueCommand(session, Array.Empty<Guid>(), 0, Guid.NewGuid()), CancellationToken.None);

        session.Queue.Should().BeEmpty();
        changed.Should().BeFalse();
        await _timeline.DidNotReceive().TrackChangedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}

public sealed class NextTrackCommandHandlerTests
{
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    private NextTrackCommandHandler CreateHandler() => new(_timeline);

    [Fact]
    public async Task Handle_AdvancesQueueAndCallsTimeline()
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
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, active, tracks[0], 0, tracks[1], Arg.Any<CancellationToken>());
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
        await _timeline.DidNotReceive().TrackChangedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}

public sealed class PreviousTrackCommandHandlerTests
{
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    private PreviousTrackCommandHandler CreateHandler() => new(_timeline);

    [Fact]
    public async Task Handle_MovesIndexBackAndCallsTimeline()
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
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, active, tracks[1], 0, tracks[0], Arg.Any<CancellationToken>());
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
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    private TrackEndedCommandHandler CreateHandler() => new(_timeline);

    [Fact]
    public async Task Handle_AdvancesQueueAndCallsTimeline()
    {
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(device);

        var changed = await CreateHandler().Handle(
            new TrackEndedCommand(session, device, tracks[0], 180_000),
            CancellationToken.None);

        session.TrackId.Should().Be(tracks[1]);
        changed.Should().BeTrue();
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, device, tracks[0], 180_000, tracks[1], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatOne_RestartsSameTrack()
    {
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { track }, 0);
        session.ClaimActiveIfNone(device);
        session.SetRepeatMode(RepeatMode.One);

        var changed = await CreateHandler().Handle(
            new TrackEndedCommand(session, device, track, 180_000),
            CancellationToken.None);

        session.TrackId.Should().Be(track);
        session.PositionMs.Should().Be(0);
        changed.Should().BeTrue();
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, device, track, 180_000, track, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatOff_LastTrack_Stops()
    {
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(new[] { track }, 0);
        session.ClaimActiveIfNone(device);

        var changed = await CreateHandler().Handle(
            new TrackEndedCommand(session, device, track, 180_000),
            CancellationToken.None);

        session.TrackId.Should().BeNull();
        session.IsPlaying.Should().BeFalse();
        changed.Should().BeTrue();
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, device, track, 180_000, null, Arg.Any<CancellationToken>());
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
        await _timeline.DidNotReceive().TrackChangedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}

public sealed class AddToQueueCommandHandlerTests
{
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    private AddToQueueCommandHandler CreateHandler() => new(_timeline);

    [Fact]
    public async Task Handle_AppendsToExistingQueue_DoesNotCallTimeline()
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
        await _timeline.DidNotReceive().TrackChangedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FirstAddToEmptyQueue_CallsTimeline()
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
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, sender, null, 0, t, Arg.Any<CancellationToken>());
    }
}

public sealed class RemoveFromQueueCommandHandlerTests
{
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    private RemoveFromQueueCommandHandler CreateHandler() => new(_timeline);

    [Fact]
    public async Task Handle_RemovingCurrentTrack_CallsTimeline()
    {
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var session = PlaybackSession.Create(Guid.NewGuid());
        session.SetQueue(tracks, 0);
        session.ClaimActiveIfNone(device);

        var changed = await CreateHandler().Handle(
            new RemoveFromQueueCommand(session, device, 0),
            CancellationToken.None);

        session.Queue.Should().ContainSingle().Which.Should().Be(tracks[1]);
        session.TrackId.Should().Be(tracks[1]);
        changed.Should().BeTrue();
        await _timeline.Received(1).TrackChangedAsync(
            session.UserId, device, tracks[0], 0, tracks[1], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemovingNonCurrentTrack_DoesNotCallTimeline()
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
        await _timeline.DidNotReceive().TrackChangedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
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
    private readonly IPlaySessionTimeline _timeline = Substitute.For<IPlaySessionTimeline>();

    [Fact]
    public async Task Handle_ClearsQueueStopsPlaybackAndCallsTimeline()
    {
        var session = PlaybackSession.Create(Guid.NewGuid());
        var device = Guid.NewGuid();
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 1);
        session.ClaimActiveIfNone(device);

        var changed = await new ClearQueueCommandHandler(_timeline).Handle(
            new ClearQueueCommand(session, device),
            CancellationToken.None);

        session.Queue.Should().BeEmpty();
        session.IsPlaying.Should().BeFalse();
        changed.Should().BeTrue();
        await _timeline.Received(1).SessionClearedAsync(
            session.UserId, 0, Arg.Any<CancellationToken>());
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
