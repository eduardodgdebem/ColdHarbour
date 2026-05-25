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

    // --- Phase 2: SetQueue now also primes the playing track ------------------

    [Fact]
    public void SetQueue_PrimesTrackIdToStartIndexAndMarksPlaying()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        session.SetQueue(tracks, startIndex: 2);

        session.TrackId.Should().Be(tracks[2]);
        session.IsPlaying.Should().BeTrue();
        session.PositionMs.Should().Be(0);
    }

    [Fact]
    public void SetQueue_EmptyTracks_ClearsTrackIdAndStopsPlaying()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { Guid.NewGuid() }, 0);

        session.SetQueue(Array.Empty<Guid>(), 0);

        session.TrackId.Should().BeNull();
        session.IsPlaying.Should().BeFalse();
    }

    // --- Phase 2: AdvanceNext / AdvancePrevious --------------------------------

    [Fact]
    public void AdvanceNext_MovesIndexAndUpdatesTrackId()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);

        session.AdvanceNext();

        session.QueueIndex.Should().Be(1);
        session.TrackId.Should().Be(tracks[1]);
        session.PositionMs.Should().Be(0);
    }

    [Fact]
    public void AdvanceNext_AtEnd_WrapsToZero()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 1);

        session.AdvanceNext();

        session.QueueIndex.Should().Be(0);
        session.TrackId.Should().Be(tracks[0]);
    }

    [Fact]
    public void AdvanceNext_EmptyQueue_Throws()
    {
        var session = PlaybackSession.Create(UserId);

        var act = () => session.AdvanceNext();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AdvancePrevious_MovesIndexBackAndUpdatesTrackId()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 2);

        session.AdvancePrevious();

        session.QueueIndex.Should().Be(1);
        session.TrackId.Should().Be(tracks[1]);
        session.PositionMs.Should().Be(0);
    }

    [Fact]
    public void AdvancePrevious_AtZero_WrapsToEnd()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);

        session.AdvancePrevious();

        session.QueueIndex.Should().Be(2);
        session.TrackId.Should().Be(tracks[2]);
    }

    [Fact]
    public void AdvancePrevious_EmptyQueue_Throws()
    {
        var session = PlaybackSession.Create(UserId);

        var act = () => session.AdvancePrevious();

        act.Should().Throw<InvalidOperationException>();
    }

    // --- Phase 2: Seek ---------------------------------------------------------

    [Fact]
    public void Seek_UpdatesPositionMs()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { TrackId }, 0);

        session.Seek(45_000);

        session.PositionMs.Should().Be(45_000);
    }

    [Fact]
    public void Seek_WithoutTrack_Throws()
    {
        var session = PlaybackSession.Create(UserId);

        var act = () => session.Seek(1_000);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Seek_NegativePosition_Throws()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { TrackId }, 0);

        var act = () => session.Seek(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // --- Phase 2: ClaimActiveIfNone -------------------------------------------

    [Fact]
    public void ClaimActiveIfNone_SetsActiveDeviceWhenNullAndATrackIsLoaded()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { TrackId }, 0);
        // SetQueue does not assign ActiveDeviceId; it's claimed independently.

        session.ClaimActiveIfNone(DeviceId);

        session.ActiveDeviceId.Should().Be(DeviceId);
    }

    [Fact]
    public void ClaimActiveIfNone_KeepsExistingActiveDevice()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { TrackId }, 0);
        session.ClaimActiveIfNone(DeviceId);

        var other = Guid.NewGuid();
        session.ClaimActiveIfNone(other);

        session.ActiveDeviceId.Should().Be(DeviceId);
    }

    // --- Phase 3: RepeatMode + Shuffle defaults --------------------------------

    [Fact]
    public void Create_DefaultsRepeatModeOffAndShuffleFalse()
    {
        var session = PlaybackSession.Create(UserId);

        session.RepeatMode.Should().Be(RepeatMode.Off);
        session.Shuffle.Should().BeFalse();
    }

    [Theory]
    [InlineData(RepeatMode.Off)]
    [InlineData(RepeatMode.All)]
    [InlineData(RepeatMode.One)]
    public void SetRepeatMode_StoresMode(RepeatMode mode)
    {
        var session = PlaybackSession.Create(UserId);

        session.SetRepeatMode(mode);

        session.RepeatMode.Should().Be(mode);
    }

    [Fact]
    public void SetShuffle_StoresFlag()
    {
        var session = PlaybackSession.Create(UserId);
        session.SetQueue(new[] { Guid.NewGuid(), Guid.NewGuid() }, 0);

        session.SetShuffle(true);

        session.Shuffle.Should().BeTrue();
    }

    // --- Phase 3: AdvanceAfterEnd matrix ---------------------------------------

    [Fact]
    public void AdvanceAfterEnd_RepeatOne_RestartsSameTrackAtZero()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);
        session.UpdatePosition(120_000);
        session.SetRepeatMode(RepeatMode.One);

        session.AdvanceAfterEnd();

        session.QueueIndex.Should().Be(0);
        session.TrackId.Should().Be(tracks[0]);
        session.PositionMs.Should().Be(0);
        session.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void AdvanceAfterEnd_RepeatOff_MidQueue_AdvancesToNext()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);
        session.SetRepeatMode(RepeatMode.Off);

        session.AdvanceAfterEnd();

        session.QueueIndex.Should().Be(1);
        session.TrackId.Should().Be(tracks[1]);
        session.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void AdvanceAfterEnd_RepeatOff_LastTrack_StopsPlayback()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 1);
        session.SetRepeatMode(RepeatMode.Off);

        session.AdvanceAfterEnd();

        session.IsPlaying.Should().BeFalse();
        session.TrackId.Should().BeNull();
    }

    [Fact]
    public void AdvanceAfterEnd_RepeatAll_LastTrack_WrapsToFirst()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 2);
        session.SetRepeatMode(RepeatMode.All);

        session.AdvanceAfterEnd();

        session.QueueIndex.Should().Be(0);
        session.TrackId.Should().Be(tracks[0]);
        session.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void AdvanceAfterEnd_EmptyQueue_NoOp()
    {
        var session = PlaybackSession.Create(UserId);

        session.AdvanceAfterEnd();

        session.TrackId.Should().BeNull();
        session.QueueIndex.Should().Be(0);
    }

    // --- Phase 3: Shuffle stability --------------------------------------------

    [Fact]
    public void AdvanceAfterEnd_Shuffle_PlaysEveryTrackExactlyOnceBeforeRepeating()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        session.SetQueue(tracks, 0);
        session.SetRepeatMode(RepeatMode.All);
        // Deterministic seed so the test is repeatable.
        session.SetShuffle(true, new Random(42));

        // Capture the first cycle's visit order (the initial track at index 0
        // counts as the first visit).
        var visited = new List<Guid> { session.TrackId!.Value };
        for (int i = 0; i < tracks.Length - 1; i++)
        {
            session.AdvanceAfterEnd(new Random(42));
            visited.Add(session.TrackId!.Value);
        }

        visited.Distinct().Should().HaveCount(tracks.Length,
            "shuffle within a cycle should not repeat any track");
        visited.Should().BeEquivalentTo(tracks);
    }

    [Fact]
    public void AdvanceAfterEnd_ShuffleRepeatOff_StopsAtEndOfShuffledCycle()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);
        session.SetRepeatMode(RepeatMode.Off);
        session.SetShuffle(true, new Random(7));

        // Step through all tracks. After the last shuffled track, the next
        // AdvanceAfterEnd must stop playback (RepeatMode.Off).
        for (int i = 0; i < tracks.Length - 1; i++)
        {
            session.AdvanceAfterEnd(new Random(7));
        }
        session.IsPlaying.Should().BeTrue("haven't reached end of cycle yet");

        session.AdvanceAfterEnd(new Random(7));

        session.IsPlaying.Should().BeFalse();
        session.TrackId.Should().BeNull();
    }

    // --- Phase 3: Next / Previous honor Shuffle when enabled ----------------

    [Fact]
    public void AdvanceNext_Shuffle_WalksShuffleOrder()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        session.SetQueue(tracks, 0);
        session.SetShuffle(true, new Random(99));

        var visited = new List<Guid> { session.TrackId!.Value };
        for (int i = 0; i < tracks.Length - 1; i++)
        {
            session.AdvanceNext();
            visited.Add(session.TrackId!.Value);
        }

        visited.Distinct().Should().HaveCount(tracks.Length,
            "Next under shuffle must visit every track exactly once within a cycle");
        visited.Should().BeEquivalentTo(tracks);
    }

    [Fact]
    public void AdvanceNext_ShuffleAtEndOfCycle_WrapsToBeginning()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);
        session.SetShuffle(true, new Random(5));

        // Walk through the whole cycle.
        session.AdvanceNext();
        session.AdvanceNext();

        // Next from the end wraps — user-clicked Next never stops.
        session.AdvanceNext();
        session.TrackId.Should().NotBeNull();
        session.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void AdvancePrevious_Shuffle_WalksBackThroughShuffleOrder()
    {
        var session = PlaybackSession.Create(UserId);
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        session.SetQueue(tracks, 0);
        session.SetShuffle(true, new Random(13));

        var first = session.TrackId!.Value;
        session.AdvanceNext();
        var second = session.TrackId!.Value;

        session.AdvancePrevious();
        session.TrackId.Should().Be(first,
            "Previous after a shuffle Next must return to the prior shuffle entry");

        session.AdvanceNext();
        session.TrackId.Should().Be(second);
    }

    [Fact]
    public void SetQueue_ResetsShuffleOrderWhenShuffleOn()
    {
        var session = PlaybackSession.Create(UserId);
        var first = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        session.SetQueue(first, 0);
        session.SetRepeatMode(RepeatMode.All);
        session.SetShuffle(true, new Random(1));

        var fresh = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        session.SetQueue(fresh, 0);

        // After a new SetQueue, the first AdvanceAfterEnd must pick from the
        // fresh queue (not the prior shuffled order).
        session.AdvanceAfterEnd(new Random(1));
        fresh.Should().Contain(session.TrackId!.Value);
    }
}
