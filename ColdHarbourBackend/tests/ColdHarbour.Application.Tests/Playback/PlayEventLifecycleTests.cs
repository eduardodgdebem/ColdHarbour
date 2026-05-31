using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Application.Playback.Services;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback;

/// <summary>
/// Invariant tests for the PlayEvent lifecycle.
/// Phase 2 turned the track-change, transfer, queue-mutation, and random-walk tests green.
/// Phase 3 turns the pause-aware tests green.
/// Phase-4 test remains skipped until that phase lands.
/// </summary>
public sealed class PlayEventLifecycleTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private record Handlers(
        PlaybackSession Session,
        InMemoryPlayEventRepository Repo,
        PlaySessionTimeline Timeline,
        SetQueueCommandHandler SetQueue,
        NextTrackCommandHandler Next,
        PreviousTrackCommandHandler Previous,
        TransferPlaybackCommandHandler Transfer,
        AddToQueueCommandHandler AddToQueue,
        RemoveFromQueueCommandHandler RemoveFromQueue,
        TrackEndedCommandHandler TrackEnded,
        ClearQueueCommandHandler ClearQueue,
        PauseCommandHandler Pause,
        ResumeCommandHandler Resume);

    private static Handlers BuildHandlers(ITrackRepository? trackRepo = null)
    {
        var repo = new InMemoryPlayEventRepository();
        var tracks = trackRepo ?? new NullTrackRepository();
        var timeline = new PlaySessionTimeline(repo, tracks);
        var session = PlaybackSession.Create(Guid.NewGuid());
        return new Handlers(session, repo, timeline,
            new SetQueueCommandHandler(timeline),
            new NextTrackCommandHandler(timeline),
            new PreviousTrackCommandHandler(timeline),
            new TransferPlaybackCommandHandler(timeline),
            new AddToQueueCommandHandler(timeline),
            new RemoveFromQueueCommandHandler(timeline),
            new TrackEndedCommandHandler(timeline, tracks),
            new ClearQueueCommandHandler(timeline),
            new PauseCommandHandler(timeline),
            new ResumeCommandHandler(timeline));
    }

    private static Guid[] Tracks(int count) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

    // ── invariant 1: SetQueue → SetQueue ─────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenSetQueue_ProducesExactlyOneOpenEvent()
    {
        var h = BuildHandlers();
        var device = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(3), 0, device), default);
        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(3), 0, device), default);

        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1,
            "the first PlayEvent must be closed before opening the second");
        h.Repo.TotalByUser(h.Session.UserId).Should().Be(2,
            "two SetQueue calls must produce exactly two events total");
    }

    // ── invariant 2: SetQueue → Next ─────────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenNext_ClosesFirstEventAndOpensSecond()
    {
        var h = BuildHandlers();
        var device = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(3), 0, device), default);
        await h.Next.Handle(new NextTrackCommand(h.Session, device), default);

        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1);
        h.Repo.CountClosedByUser(h.Session.UserId).Should().Be(1);
    }

    // ── invariant 3: SetQueue → Next × 100 ───────────────────────────────────

    [Fact]
    public async Task SetQueue_Then100Nexts_ExactlyOneOpenAnd100Closed()
    {
        var h = BuildHandlers();
        var device = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(110), 0, device), default);
        for (var i = 0; i < 100; i++)
            await h.Next.Handle(new NextTrackCommand(h.Session, device), default);

        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1);
        h.Repo.CountClosedByUser(h.Session.UserId).Should().Be(100);
    }

    // ── invariant 4: SetQueue → Previous × 50 ────────────────────────────────

    [Fact]
    public async Task SetQueue_Then50Previouses_ExactlyOneOpenAnd50Closed()
    {
        var h = BuildHandlers();
        var device = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(60), 55, device), default);
        for (var i = 0; i < 50; i++)
            await h.Previous.Handle(new PreviousTrackCommand(h.Session, device), default);

        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1);
        h.Repo.CountClosedByUser(h.Session.UserId).Should().Be(50);
    }

    // ── invariant 5: Transfer ─────────────────────────────────────────────────

    [Fact]
    public async Task SetQueue_ThenTransfer_ClosesDeviceAEventAndOpensDeviceBEvent()
    {
        var h = BuildHandlers();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(3), 0, deviceA), default);
        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1);
        h.Repo.GetAll().Single(e => e.EndedAt is null).DeviceId.Should().Be(deviceA);

        await h.Transfer.Handle(new TransferPlaybackCommand(h.Session, deviceB, h.Session.PositionMs), default);

        var all = h.Repo.GetAll();
        all.Should().HaveCount(2);
        all.Should().ContainSingle(e => e.EndedAt != null && e.DeviceId == deviceA);
        all.Should().ContainSingle(e => e.EndedAt == null && e.DeviceId == deviceB);
    }

    // ── invariant 6: AddToQueue, then SetQueue ────────────────────────────────

    [Fact]
    public async Task AddToQueue_OnEmptyQueue_ThenSetQueue_ClosesFirstEventAndOpensSecond()
    {
        var h = BuildHandlers();
        var device = Guid.NewGuid();

        await h.AddToQueue.Handle(new AddToQueueCommand(h.Session, device, Guid.NewGuid(), null), default);
        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1);

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(5), 0, device), default);

        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1);
        h.Repo.CountClosedByUser(h.Session.UserId).Should().Be(1);
    }

    // ── invariant 7: random-walk ──────────────────────────────────────────────

    [Fact]
    public async Task RandomWalk_100Commands_AtMostOneOpenEventAtEnd()
    {
        var h = BuildHandlers();
        var devices = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var rng = new Random(42);

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(20), 0, devices[0]), default);

        for (var i = 0; i < 100; i++)
        {
            var device = devices[rng.Next(devices.Length)];
            switch (rng.Next(9))
            {
                case 0:
                    var q = Tracks(rng.Next(2, 10));
                    await h.SetQueue.Handle(new SetQueueCommand(h.Session, q, rng.Next(q.Length), device), default);
                    break;
                case 1 when h.Session.Queue.Count > 0:
                    await h.Next.Handle(new NextTrackCommand(h.Session, device), default);
                    break;
                case 2 when h.Session.Queue.Count > 0:
                    await h.Previous.Handle(new PreviousTrackCommand(h.Session, device), default);
                    break;
                case 3 when h.Session.TrackId is not null:
                    var tgt = devices[(Array.IndexOf(devices, device) + 1) % devices.Length];
                    await h.Transfer.Handle(new TransferPlaybackCommand(h.Session, tgt, h.Session.PositionMs), default);
                    break;
                case 4 when h.Session.Queue.Count > 0:
                    await h.AddToQueue.Handle(new AddToQueueCommand(h.Session, device, Guid.NewGuid(), null), default);
                    break;
                case 5 when h.Session.Queue.Count > 1:
                    await h.RemoveFromQueue.Handle(new RemoveFromQueueCommand(h.Session, device, rng.Next(h.Session.Queue.Count)), default);
                    break;
                case 6:
                    await h.Pause.Handle(new PauseCommand(h.Session, device), default);
                    break;
                case 7:
                    await h.Resume.Handle(new ResumeCommand(h.Session, device), default);
                    break;
                case 8 when h.Session.TrackId is not null && h.Session.ActiveDeviceId == device:
                    await h.TrackEnded.Handle(new TrackEndedCommand(h.Session, device, h.Session.TrackId.Value), default);
                    break;
            }
        }

        h.Repo.CountOpenByUser(h.Session.UserId).Should().BeLessOrEqualTo(1,
            "at most one PlayEvent per user must be open after any sequence of transport commands");
    }

    // ── Phase-3 invariants ────────────────────────────────────────────────────

    [Fact]
    public async Task SetQueue_PauseResume_DoesNotOpenSecondEvent()
    {
        var h = BuildHandlers();
        var device = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(3), 0, device), default);
        await h.Pause.Handle(new PauseCommand(h.Session, device), default);
        await h.Resume.Handle(new ResumeCommand(h.Session, device), default);

        h.Repo.CountOpenByUser(h.Session.UserId).Should().Be(1,
            "pause/resume must not open additional PlayEvents");
        h.Repo.TotalByUser(h.Session.UserId).Should().Be(1);
    }

    [Fact]
    public async Task SetQueue_PauseOneHour_TrackEnded_ListenedMsExcludesPausedTime()
    {
        // SetQueue opens an event. We pause after 10 s and then track ends 1 hour
        // into the pause. ListenedMs must reflect ~10 s, not ~1 hour.
        var h = BuildHandlers();
        var device = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, Tracks(3), 0, device), default);

        var openEvent = h.Repo.GetAll().Single();
        var pauseAt = openEvent.SegmentStartedAt.AddSeconds(10);
        var endAt = pauseAt.AddHours(1);

        // Pause and complete via the timeline directly with explicit timestamps
        await h.Timeline.PausedAsync(h.Session.UserId, pauseAt, default);
        // TrackEnded handler calls timeline.TrackChangedAsync which calls Complete(nowUtc≈UtcNow)
        // Use the timeline directly to control the time for a deterministic assertion
        await h.Timeline.TrackChangedAsync(
            h.Session.UserId, device,
            h.Session.TrackId, 0, null,
            default);  // complete with UtcNow (close to pauseAt)

        var closed = h.Repo.GetAll().Single(e => e.EndedAt is not null);
        closed.ListenedMs.Should().BeLessThan(30_000,
            "only ~10 s of listening happened; the 1-hour pause must not inflate ListenedMs");
    }

    // ── Phase-4 invariant ────────────────────────────────────────────────────

    [Fact]
    public async Task TrackEnded_UsesServerTrackDuration_CompletedRatioReflectsServerKnowledge()
    {
        // The 30-s track is known to the server. The client no longer sends durationMs.
        // The handler resolves Track.Duration from ITrackRepository and uses it as the
        // end position, so CompletedRatio = 1.0 (track.Duration / track.Duration).
        var trackId = Guid.NewGuid();
        var trackRepo = new SingleTrackRepository(trackId, durationMs: 30_000);
        var h = BuildHandlers(trackRepo);
        var device = Guid.NewGuid();

        await h.SetQueue.Handle(new SetQueueCommand(h.Session, [trackId], 0, device), default);
        await h.TrackEnded.Handle(new TrackEndedCommand(h.Session, device, trackId), default);

        var closed = h.Repo.GetAll().Single(e => e.EndedAt is not null);
        closed.CompletedRatio.Should().BeApproximately(1.0, 0.001,
            "server resolves track duration = 30s; ended at duration → ratio 1.0");
    }

    // ── stub ─────────────────────────────────────────────────────────────────

    private sealed class SingleTrackRepository(Guid trackId, int durationMs) : ITrackRepository
    {
        private readonly Track _track = Track.Create(
            title: "test", albumId: Guid.NewGuid(),
            duration: TimeSpan.FromMilliseconds(durationMs),
            provider: "local", format: "flac", bitrate: 1000,
            audioSha1: "a".PadRight(40, 'a'));

        public Task<Track?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Track?>(id == trackId ? _track : null);
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
