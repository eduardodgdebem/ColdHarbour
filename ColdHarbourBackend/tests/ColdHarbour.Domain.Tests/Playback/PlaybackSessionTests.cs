using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Playback;

public sealed class PlaybackSessionTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly Guid TrackId = Guid.NewGuid();

    [Fact]
    public void Create_HasNoActiveState()
    {
        var session = PlaybackSession.Create(UserId);

        session.UserId.Should().Be(UserId);
        session.ActiveDeviceId.Should().BeNull();
        session.TrackId.Should().BeNull();
        session.PositionMs.Should().Be(0);
        session.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void Start_SetsDeviceAndTrackAndPlaying()
    {
        var session = PlaybackSession.Create(UserId);

        session.Start(DeviceId, TrackId);

        session.ActiveDeviceId.Should().Be(DeviceId);
        session.TrackId.Should().Be(TrackId);
        session.PositionMs.Should().Be(0);
        session.IsPlaying.Should().BeTrue();
        session.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void UpdatePosition_ChangesPositionMs()
    {
        var session = PlaybackSession.Create(UserId);
        session.Start(DeviceId, TrackId);

        session.UpdatePosition(45_000);

        session.PositionMs.Should().Be(45_000);
    }

    [Fact]
    public void Pause_SetsIsPlayingFalse()
    {
        var session = PlaybackSession.Create(UserId);
        session.Start(DeviceId, TrackId);

        session.Pause();

        session.IsPlaying.Should().BeFalse();
        session.PositionMs.Should().Be(0);
    }

    [Fact]
    public void Resume_WithActiveTrack_SetsIsPlayingTrue()
    {
        var session = PlaybackSession.Create(UserId);
        session.Start(DeviceId, TrackId);
        session.Pause();

        session.Resume();

        session.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void Resume_WithNoTrack_Throws()
    {
        var session = PlaybackSession.Create(UserId);

        var act = () => session.Resume();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Transfer_ChangesActiveDevice_AndSetsPositionAndPlaying()
    {
        var session = PlaybackSession.Create(UserId);
        session.Start(DeviceId, TrackId);
        session.UpdatePosition(30_000);

        var newDevice = Guid.NewGuid();
        session.Transfer(newDevice, 30_000);

        session.ActiveDeviceId.Should().Be(newDevice);
        session.PositionMs.Should().Be(30_000);
        session.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void Transfer_WithNoActiveTrack_Throws()
    {
        var session = PlaybackSession.Create(UserId);

        var act = () => session.Transfer(Guid.NewGuid(), 0);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Clear_ResetsAllState()
    {
        var session = PlaybackSession.Create(UserId);
        session.Start(DeviceId, TrackId);

        session.Clear();

        session.ActiveDeviceId.Should().BeNull();
        session.TrackId.Should().BeNull();
        session.PositionMs.Should().Be(0);
        session.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void Create_HasEmptyQueueAtIndexZero()
    {
        var session = PlaybackSession.Create(UserId);

        session.Queue.Should().BeEmpty();
        session.QueueIndex.Should().Be(0);
    }

    [Fact]
    public void SetQueue_StoresTracksAndStartIndex()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        session.SetQueue(tracks, startIndex: 1);

        session.Queue.Should().Equal(tracks);
        session.QueueIndex.Should().Be(1);
        session.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SetQueue_DefaultsStartIndexToZero()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        session.SetQueue(tracks);

        session.QueueIndex.Should().Be(0);
    }

    [Fact]
    public void SetQueue_EmptyTracks_ClearsQueueAndResetsIndex()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { Guid.NewGuid() }, 0);

        session.SetQueue(Array.Empty<Guid>(), 0);

        session.Queue.Should().BeEmpty();
        session.QueueIndex.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void SetQueue_StartIndexOutOfRange_Throws(int badIndex)
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var act = () => session.SetQueue(tracks, badIndex);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetQueue_EmptyTracksWithNonZeroIndex_Throws()
    {
        var session = PlaybackSession.Create(UserId);

        var act = () => session.SetQueue(Array.Empty<Guid>(), 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MoveTo_ChangesQueueIndex()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);

        session.MoveTo(2);

        session.QueueIndex.Should().Be(2);
        session.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void MoveTo_OutOfRange_Throws(int badIndex)
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);

        var act = () => session.MoveTo(badIndex);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MoveTo_EmptyQueue_Throws()
    {
        var session = PlaybackSession.Create(UserId);

        var act = () => session.MoveTo(0);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Clear_ResetsQueueAndIndex()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 1);

        session.Clear();

        session.Queue.Should().BeEmpty();
        session.QueueIndex.Should().Be(0);
    }
}
