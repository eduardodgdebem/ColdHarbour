using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Application.Playback.Services;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback;

/// <summary>
/// Red-contract tests for the PlayEvent lifecycle.
/// Phase 2 turns the track-change, transfer, queue-mutation, and random-walk tests green.
/// Phase-3 and Phase-4 tests remain skipped until those phases land.
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
        var timeline = new PlaySessionTimeline(repo, new NullTrackRepository());
        var session = PlaybackSession.Create(Guid.NewGuid());
        return (session, repo,
            new SetQueueCommandHandler(timeline),
            new NextTrackCommandHandler(timeline),
            new PreviousTrackCommandHandler(timeline),
            new TransferPlaybackCommandHandler(timeline),
            new AddToQueueCommandHandler(timeline),
            new RemoveFromQueueCommandHandler(timeline),
            new TrackEndedCommandHandler(timeline),
            new ClearQueueCommandHandler(timeline));
    }

    private static Guid[] Tracks(int count) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

    // ── invariant 1: SetQueue → SetQueue ─────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenSetQueue_ProducesExactlyOneOpenEvent()
    {
        var (session, repo, setQueue, _, _, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var firstQueue = Tracks(3);
        var secondQueue = Tracks(3);

        await setQueue.Handle(new SetQueueCommand(session, firstQueue, 0, device), default);
        await setQueue.Handle(new SetQueueCommand(session, secondQueue, 0, device), default);

        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "the first PlayEvent must be closed before opening the second");
        repo.TotalByUser(session.UserId).Should().Be(2,
            "two SetQueue calls must produce exactly two events total");
    }

    // ── invariant 2: SetQueue → Next ─────────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenNext_ClosesFirstEventAndOpensSecond()
    {
        var (session, repo, setQueue, next, _, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var queue = Tracks(3);

        await setQueue.Handle(new SetQueueCommand(session, queue, 0, device), default);
        await next.Handle(new NextTrackCommand(session, device), default);

        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "only the second track's PlayEvent must be open");
        repo.CountClosedByUser(session.UserId).Should().Be(1,
            "the first track's PlayEvent must be closed");
    }

    // ── invariant 3: SetQueue → Next × 100 ───────────────────────────────────

    [Fact]
    public async Task SetQueue_Then100Nexts_ExactlyOneOpenAnd100Closed()
    {
        var (session, repo, setQueue, next, _, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var queue = Tracks(110);

        await setQueue.Handle(new SetQueueCommand(session, queue, 0, device), default);

        for (var i = 0; i < 100; i++)
            await next.Handle(new NextTrackCommand(session, device), default);

        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "only the current track's PlayEvent must be open");
        repo.CountClosedByUser(session.UserId).Should().Be(100,
            "each of the 100 skipped tracks must have a closed PlayEvent");
    }

    // ── invariant 4: SetQueue → Previous × 50 ────────────────────────────────

    [Fact]
    public async Task SetQueue_Then50Previouses_ExactlyOneOpenAnd50Closed()
    {
        var (session, repo, setQueue, _, previous, _, _, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var queue = Tracks(60);

        await setQueue.Handle(new SetQueueCommand(session, queue, 55, device), default);

        for (var i = 0; i < 50; i++)
            await previous.Handle(new PreviousTrackCommand(session, device), default);

        repo.CountOpenByUser(session.UserId).Should().Be(1);
        repo.CountClosedByUser(session.UserId).Should().Be(50);
    }

    // ── invariant 5: Transfer ─────────────────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenTransfer_ClosesDeviceAEventAndOpensDeviceBEvent()
    {
        var (session, repo, setQueue, _, _, transfer, _, _, _, _) = BuildHandlers();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var queue = Tracks(3);

        await setQueue.Handle(new SetQueueCommand(session, queue, 0, deviceA), default);
        repo.CountOpenByUser(session.UserId).Should().Be(1);
        var openBeforeTransfer = repo.GetAll().Single(e => e.EndedAt is null);
        openBeforeTransfer.DeviceId.Should().Be(deviceA);

        await transfer.Handle(new TransferPlaybackCommand(session, deviceB, session.PositionMs), default);

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
        var (session, repo, setQueue, _, _, _, addToQueue, _, _, _) = BuildHandlers();
        var device = Guid.NewGuid();
        var trackA = Guid.NewGuid();
        var albumQueue = Tracks(5);

        await addToQueue.Handle(new AddToQueueCommand(session, device, trackA, null), default);
        repo.CountOpenByUser(session.UserId).Should().Be(1);

        await setQueue.Handle(new SetQueueCommand(session, albumQueue, 0, device), default);

        repo.CountOpenByUser(session.UserId).Should().Be(1,
            "trackA's event must be closed; only the new album track's event is open");
        repo.CountClosedByUser(session.UserId).Should().Be(1,
            "trackA's event must now be closed");
    }

    // ── invariant 7: random-walk ──────────────────────────────────────────────

    [Fact]
    public async Task RandomWalk_100Commands_AtMostOneOpenEventAtEnd()
    {
        var (session, repo, setQueue, next, previous, transfer, addToQueue, removeFromQueue, trackEnded, clearQueue) =
            BuildHandlers();

        var devices = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var rng = new Random(42);

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
                    session.Pause();
                    break;

                case 7:
                    if (session.TrackId is not null)
                        session.Resume();
                    break;

                case 8 when session.TrackId is not null && session.ActiveDeviceId == device:
                    await trackEnded.Handle(
                        new TrackEndedCommand(session, device, session.TrackId.Value, 180_000), default);
                    break;
            }
        }

        repo.CountOpenByUser(session.UserId).Should().BeLessOrEqualTo(1,
            "at most one PlayEvent per user must be open after any sequence of transport commands");
    }

    // ── Phase-3 invariants (skipped — require PlayEvent.PauseListening / ListenedMs) ──

    [Fact(Skip = "Phase 3 — PlayEvent.PauseListening and PlayEvent.ListenedMs not yet added")]
    public async Task SetQueue_PauseResume_DoesNotOpenSecondEvent()
    {
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
        await Task.CompletedTask;
        Assert.Fail("Phase 3 — PlayEvent.ListenedMs not implemented");
    }

    // ── Phase-4 invariant (skipped — requires handler to call ITrackRepository) ──

    [Fact(Skip = "Phase 4 — TrackEndedCommandHandler must use Track.Duration not client DurationMs")]
    public async Task TrackEnded_InflatedClientDurationMs_ClampedToTrackDuration()
    {
        await Task.CompletedTask;
        Assert.Fail("Phase 4 — handler does not yet validate against Track.Duration");
    }

    // ── stub ─────────────────────────────────────────────────────────────────

    private sealed class NullTrackRepository : ITrackRepository
    {
        public Task<Track?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Track?>(null);
        public Task<Track?> FindByAudioSha1Async(string a, CancellationToken ct = default) => Task.FromResult<Track?>(null);
        public Task<Artist?> FindArtistByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
        public Task<Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
        public Task<Album?> FindAlbumByArtistAndTitleAsync(Guid id, string t, CancellationToken ct = default) => Task.FromResult<Album?>(null);
        public Task<Album?> FindAlbumByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Album?>(null);
        public Task<int> CountTracksByAlbumIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> CountAlbumsByArtistIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddArtistAsync(Artist a, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddAlbumAsync(Album a, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddTrackAsync(Track t, CancellationToken ct = default) => Task.CompletedTask;
        public void RemoveTrack(Track t) { }
        public void RemoveAlbum(Album a) { }
        public void RemoveArtist(Artist a) { }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default) => Task.FromResult(new List<Track>());
    }
}
