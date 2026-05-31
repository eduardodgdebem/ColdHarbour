using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback;

public sealed class CloseOrphanedPlayEventsCommandTests
{
    // Pass Before = UtcNow + 1s so all freshly-created events qualify as orphans.
    private static readonly DateTimeOffset Soon = DateTimeOffset.UtcNow.AddSeconds(1);

    private static CloseOrphanedPlayEventsCommandHandler Build(
        InMemoryPlayEventRepository repo,
        ITrackRepository? tracks = null) =>
        new(repo, tracks ?? new NullTrackRepository());

    // ── basic close ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ClosesAllOrphanedEvents()
    {
        var repo = new InMemoryPlayEventRepository();
        var userId = Guid.NewGuid();

        await repo.AddAsync(PlayEvent.Begin(userId, Guid.NewGuid(), Guid.NewGuid()));
        await repo.AddAsync(PlayEvent.Begin(userId, Guid.NewGuid(), Guid.NewGuid()));
        await repo.AddAsync(PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        var count = await Build(repo).Handle(
            new CloseOrphanedPlayEventsCommand(Soon), default);

        count.Should().Be(3);
        repo.GetAll().Should().AllSatisfy(e => e.EndedAt.Should().NotBeNull());
        repo.GetAll().Should().AllSatisfy(e => e.BackfilledAt.Should().NotBeNull());
    }

    [Fact]
    public async Task Handle_Idempotent_SecondRunReturnsZero()
    {
        var repo = new InMemoryPlayEventRepository();
        await repo.AddAsync(PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        var handler = Build(repo);
        await handler.Handle(new CloseOrphanedPlayEventsCommand(Soon), default);
        var second = await handler.Handle(new CloseOrphanedPlayEventsCommand(Soon), default);

        second.Should().Be(0, "all events are already closed; nothing to backfill again");
    }

    [Fact]
    public async Task Handle_DoesNotTouchEventsThatAreAlreadyClosed()
    {
        var repo = new InMemoryPlayEventRepository();
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        ev.Complete(60_000, 60_000);
        await repo.AddAsync(ev);

        var count = await Build(repo).Handle(
            new CloseOrphanedPlayEventsCommand(Soon), default);

        count.Should().Be(0);
        ev.BackfilledAt.Should().BeNull("already closed events are not re-touched");
    }

    [Fact]
    public async Task Handle_DoesNotTouchEventsThatStartedAfterCutoff()
    {
        var repo = new InMemoryPlayEventRepository();
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await repo.AddAsync(ev);

        // Pass a cutoff BEFORE the event's StartedAt
        var beforeEvent = ev.StartedAt.AddSeconds(-1);
        var count = await Build(repo).Handle(
            new CloseOrphanedPlayEventsCommand(beforeEvent), default);

        count.Should().Be(0, "event is newer than the cutoff; must not be backfilled");
    }

    [Fact]
    public async Task Handle_ReturnsZeroWhenNoOrphans()
    {
        var repo = new InMemoryPlayEventRepository();
        var count = await Build(repo).Handle(
            new CloseOrphanedPlayEventsCommand(Soon), default);
        count.Should().Be(0);
    }

    // ── heuristic values ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithKnownTrack_ListenedMsIsTrackDuration()
    {
        var trackId = Guid.NewGuid();
        var repo = new InMemoryPlayEventRepository();
        await repo.AddAsync(PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), trackId));

        var trackRepo = new SingleTrackRepository(trackId, durationMs: 180_000);
        var count = await Build(repo, trackRepo).Handle(
            new CloseOrphanedPlayEventsCommand(Soon), default);

        count.Should().Be(1);
        var closed = repo.GetAll().Single();
        closed.ListenedMs.Should().Be(180_000,
            "track is 3 min; heuristic sets ListenedMs = Track.Duration");
    }

    [Fact]
    public async Task Handle_WithUnknownTrack_CapIsOneDay()
    {
        var repo = new InMemoryPlayEventRepository();
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await repo.AddAsync(ev);

        await Build(repo).Handle(new CloseOrphanedPlayEventsCommand(Soon), default);

        var closed = repo.GetAll().Single();
        var wallClock = (closed.EndedAt!.Value - ev.StartedAt).TotalHours;
        wallClock.Should().BeLessThanOrEqualTo(24.01,
            "with no track info the cap is 1 day");
    }

    // ── stubs ────────────────────────────────────────────────────────────────

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
        public Task<Artist?> FindArtistByNameAsync(string n, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
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
        public Task<Artist?> FindArtistByNameAsync(string n, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
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
