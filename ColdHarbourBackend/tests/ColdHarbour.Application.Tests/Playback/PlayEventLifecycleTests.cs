using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback;

/// <summary>
/// Red-contract tests for the PlayEvent lifecycle.
/// Every test below that is NOT skipped asserts an invariant that the CURRENT code violates.
/// Phases 2–4 will turn them green one group at a time.
///
/// Skip tags mark tests whose assertions require domain methods or fields that do not exist yet;
/// those are added in the phase listed in the skip reason.
/// </summary>
public sealed class PlayEventLifecycleTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (PlaybackSession session, InMemoryPlayEventRepository repo,
        SetQueueCommandHandler setQueue,
        NextTrackCommandHandler next,
        PreviousTrackCommandHandler previous,
        TransferPlaybackCommandHandler transfer,
        AddToQueueCommandHandler addToQueue,
        RemoveFromQueueCommandHandler removeFromQueue,
        TrackEndedCommandHandler trackEnded,
        ClearQueueCommandHandler clearQueue)
        BuildHandlers()
    {
        var repo = new InMemoryPlayEventRepository();
        var session = PlaybackSession.Create(Guid.NewGuid());
        return (session, repo,
            new SetQueueCommandHandler(repo),
            new NextTrackCommandHandler(repo),
            new PreviousTrackCommandHandler(repo),
            new TransferPlaybackCommandHandler(),
            new AddToQueueCommandHandler(repo),
            new RemoveFromQueueCommandHandler(repo),
            new TrackEndedCommandHandler(repo),
            new ClearQueueCommandHandler());
    }

    private static Guid[] Tracks(int count) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

    // ── invariant 1: SetQueue → SetQueue ─────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenSetQueue_ProducesExactlyOneOpenEvent()
    {
        // ARRANGE — two consecutive SetQueue calls (e.g. user picks a different album).
        // The first call opens a PlayEvent for the old track.
        // The second call MUST close it before opening a new one.
        // Current code: opens a second event without closing the first → FAILS HERE.
        var (session, repo, setQueue, _, _, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var firstQueue = Tracks(3);
        var secondQueue = Tracks(3);

        // ACT
        await setQueue.Handle(new SetQueueCommand(session, firstQueue, 0, device), default);
        await setQueue.Handle(new SetQueueCommand(session, secondQueue, 0, device), default);

        // ASSERT
        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "the first PlayEvent must be closed before opening the second");
        repo.TotalByUser(session.UserId).Should().Be(2,
            "two SetQueue calls must produce exactly two events total");
    }

    // ── invariant 2: SetQueue → Next ─────────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenNext_ClosesFirstEventAndOpensSecond()
    {
        // ARRANGE — user picks a playlist (SetQueue), then skips (Next).
        // SetQueue opens event for track[0]; Next MUST close it, then open one for track[1].
        // Current code: Next opens a new event without closing the prior → FAILS HERE.
        var (session, repo, setQueue, next, _, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var queue = Tracks(3);

        // ACT
        await setQueue.Handle(new SetQueueCommand(session, queue, 0, device), default);
        await next.Handle(new NextTrackCommand(session, device), default);

        // ASSERT
        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "only the second track's PlayEvent must be open");
        repo.CountClosedByUser(session.UserId).Should().Be(1,
            "the first track's PlayEvent must be closed");
    }

    // ── invariant 3: SetQueue → Next × 100 ───────────────────────────────────

    [Fact]
    public async Task SetQueue_Then100Nexts_ExactlyOneOpenAnd100Closed()
    {
        // ARRANGE — simulate a user hammering Next through a 110-track playlist.
        // Each Next must close the prior event and open a fresh one.
        // Current code: 101 open events after 100 nexts → FAILS HERE.
        var (session, repo, setQueue, next, _, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var queue = Tracks(110);

        await setQueue.Handle(new SetQueueCommand(session, queue, 0, device), default);

        for (var i = 0; i < 100; i++)
            await next.Handle(new NextTrackCommand(session, device), default);

        // ASSERT
        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "only the current track's PlayEvent must be open");
        repo.CountClosedByUser(session.UserId).Should().Be(100,
            "each of the 100 skipped tracks must have a closed PlayEvent");
    }

    // ── invariant 4: SetQueue → Previous × 50 ────────────────────────────────

    [Fact]
    public async Task SetQueue_Then50Previouses_ExactlyOneOpenAnd50Closed()
    {
        // ARRANGE — start near the end of the queue, then hit Previous 50 times.
        // Each Previous must close the prior event and open a fresh one.
        // Current code: 51 open events → FAILS HERE.
        var (session, repo, setQueue, _, previous, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var queue = Tracks(60);

        await setQueue.Handle(new SetQueueCommand(session, queue, 55, device), default);

        for (var i = 0; i < 50; i++)
            await previous.Handle(new PreviousTrackCommand(session, device), default);

        // ASSERT
        repo.CountOpenByUser(session.UserId).Should().Be(1);
        repo.CountClosedByUser(session.UserId).Should().Be(50);
    }

    // ── invariant 5: Transfer ─────────────────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenTransfer_ClosesDeviceAEventAndOpensDeviceBEvent()
    {
        // ARRANGE — device A is playing; device B calls Transfer to pull playback.
        // Transfer MUST close the event attributed to device A and open a new one
        // attributed to device B.
        // Current code: TransferPlaybackCommandHandler does not touch PlayEvents → FAILS HERE.
        var (session, repo, setQueue, _, _, transfer, _, _, _, _) = BuildHandlers();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var queue = Tracks(3);

        await setQueue.Handle(new SetQueueCommand(session, queue, 0, deviceA), default);
        // Confirm device A is active and event is open for A
        repo.CountOpenByUser(session.UserId).Should().Be(1);
        var openBeforeTransfer = repo.GetAll().Single(e => e.EndedAt is null);
        openBeforeTransfer.DeviceId.Should().Be(deviceA);

        // ACT — device B pulls playback to itself
        await transfer.Handle(new TransferPlaybackCommand(session, deviceB, session.PositionMs), default);

        // ASSERT
        var allEvents = repo.GetAll();
        allEvents.Should().HaveCount(2, "transfer must close the old event and open a new one");
        allEvents.Should().ContainSingle(e => e.EndedAt != null && e.DeviceId == deviceA,
            "device A's event must be closed");
        allEvents.Should().ContainSingle(e => e.EndedAt == null && e.DeviceId == deviceB,
            "device B's event must be open");
    }

    // ── invariant 6: AddToQueue opening event, then SetQueue closes it ────────

    [Fact]
    public async Task AddToQueue_OnEmptyQueue_ThenSetQueue_ClosesFirstEventAndOpensSecond()
    {
        // ARRANGE — user adds first track to empty queue (opens event for A),
        // then immediately picks a whole album via SetQueue (must close A's event, open for B).
        // Current code: SetQueueCommandHandler opens without closing → FAILS HERE.
        var (session, repo, setQueue, _, _, _, addToQueue, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var trackA = Guid.NewGuid();
        var albumQueue = Tracks(5);

        await addToQueue.Handle(new AddToQueueCommand(session, device, trackA, null), default);
        // One open event for trackA
        repo.CountOpenByUser(session.UserId).Should().Be(1);

        // ACT — user replaces the queue with a new album
        await setQueue.Handle(new SetQueueCommand(session, albumQueue, 0, device), default);

        // ASSERT
        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "trackA's event must be closed; only the new album track's event is open");
        repo.CountClosedByUser(session.UserId).Should().Be(1,
            "trackA's event must now be closed");
    }

    // ── invariant 7: random-walk ──────────────────────────────────────────────

    [Fact]
    public async Task RandomWalk_100Commands_AtMostOneOpenEventAtEnd()
    {
        // ARRANGE — deterministic random-walk across all transport commands from 3 devices.
        // After any number of commands the invariant must hold: ≤ 1 open PlayEvent per user.
        // Current code: each Next/Previous/SetQueue leaks an open event → FAILS HERE.
        var (session, repo, setQueue, next, previous, transfer, addToQueue, removeFromQueue, trackEnded, clearQueue) =
            BuildHandlers();

        var devices = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var rng = new Random(42); // deterministic seed

        // Seed an initial queue so transport commands have something to work with.
        var initialQueue = Tracks(20);
        await setQueue.Handle(new SetQueueCommand(session, initialQueue, 0, devices[0]), default);

        for (var i = 0; i < 100; i++)
        {
            var device = devices[rng.Next(devices.Length)];
            var command = rng.Next(9);

            switch (command)
            {
                case 0:
                    var newQueue = Tracks(rng.Next(2, 10));
                    await setQueue.Handle(
                        new SetQueueCommand(session, newQueue, rng.Next(newQueue.Length), device), default);
                    break;

                case 1 when session.Queue.Count > 0:
                    await next.Handle(new NextTrackCommand(session, device), default);
                    break;

                case 2 when session.Queue.Count > 0:
                    await previous.Handle(new PreviousTrackCommand(session, device), default);
                    break;

                case 3 when session.TrackId is not null:
                    // Transfer to a different device
                    var targetDevice = devices[(Array.IndexOf(devices, device) + 1) % devices.Length];
                    await transfer.Handle(
                        new TransferPlaybackCommand(session, targetDevice, session.PositionMs), default);
                    break;

                case 4 when session.Queue.Count > 0:
                    await addToQueue.Handle(
                        new AddToQueueCommand(session, device, Guid.NewGuid(), null), default);
                    break;

                case 5 when session.Queue.Count > 1:
                    var removeIdx = rng.Next(session.Queue.Count);
                    await removeFromQueue.Handle(
                        new RemoveFromQueueCommand(session, device, removeIdx), default);
                    break;

                case 6:
                    // Simulate Pause (direct domain call — no MediatR handler today)
                    session.Pause();
                    break;

                case 7:
                    // Simulate Resume (direct domain call — no MediatR handler today)
                    if (session.TrackId is not null)
                        session.Resume();
                    break;

                case 8 when session.TrackId is not null && session.ActiveDeviceId == device:
                    // TrackEnded — must come from active device and match current track
                    await trackEnded.Handle(
                        new TrackEndedCommand(session, device, session.TrackId.Value, 180_000), default);
                    break;
            }
        }

        // ASSERT
        repo.CountOpenByUser(session.UserId).Should().BeLessOrEqualTo(1,
            "at most one PlayEvent per user must be open after any sequence of transport commands");
    }

    // ── Phase-3 invariants (skipped — require PlayEvent.PauseListening / ListenedMs) ──

    [Fact(Skip = "Phase 3 — PlayEvent.PauseListening and PlayEvent.ListenedMs not yet added")]
    public async Task SetQueue_PauseResume_DoesNotOpenSecondEvent()
    {
        // ARRANGE — pause and resume must NOT open a second PlayEvent;
        // they should call PlayEvent.PauseListening / ResumeListening on the existing open event.
        // This test stays red until PlaySessionTimeline.PausedAsync / ResumedAsync are wired in Phase 3.
        var (session, repo, setQueue, _, _, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var queue = Tracks(3);

        await setQueue.Handle(new SetQueueCommand(session, queue, 0, device), default);
        session.Pause();
        session.Resume();

        repo.CountOpenByUser(session.UserId).Should().Be(1);
        repo.TotalByUser(session.UserId).Should().Be(1,
            "pause/resume must not open additional PlayEvents");
    }

    [Fact(Skip = "Phase 3 — PlayEvent.ListenedMs not yet added")]
    public async Task SetQueue_PauseOneHour_TrackEnded_ListenedMsExcludesPausedTime()
    {
        // ARRANGE — user pauses for one hour; when the track ends the ListenedMs recorded
        // must reflect only the pre-pause listening window, not the wall-clock hour.
        // Requires PlayEvent.PauseListening(DateTime) and PlayEvent.ListenedMs.
        // See Phase 3 domain changes for the full implementation.
        await Task.CompletedTask; // compilation placeholder
        Assert.Fail("Phase 3 — PlayEvent.ListenedMs not implemented");
    }

    // ── Phase-4 invariant (skipped — requires handler to call ITrackRepository) ──

    [Fact(Skip = "Phase 4 — TrackEndedCommandHandler must use Track.Duration not client DurationMs")]
    public async Task TrackEnded_InflatedClientDurationMs_ClampedToTrackDuration()
    {
        // ARRANGE — a track is 30 s; the client sends durationMs = 1_800_000 (30 min).
        // The handler MUST look up Track.Duration via ITrackRepository and pass that to
        // PlayEvent.Complete, not the client-supplied value.
        // Requires Phase 4 changes to TrackEndedCommandHandler and DurationMs removal.
        await Task.CompletedTask; // compilation placeholder
        Assert.Fail("Phase 4 — handler does not yet validate against Track.Duration");
    }
}
