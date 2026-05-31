using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Application.Playback.Services;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback;

/// <summary>
/// Unit tests for PlaySessionTimeline covering all three port methods.
/// Drives the TDD cycle for Phase 2.
/// </summary>
public sealed class PlaySessionTimelineTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (PlaySessionTimeline timeline, InMemoryPlayEventRepository repo) Build(
        ITrackRepository? tracks = null)
    {
        var repo = new InMemoryPlayEventRepository();
        var timeline = new PlaySessionTimeline(repo, tracks ?? new NullTrackRepository());
        return (timeline, repo);
    }

    private static Guid UserId() => Guid.NewGuid();
    private static Guid Device() => Guid.NewGuid();
    private static Guid TrackId() => Guid.NewGuid();

    // ── TrackChangedAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task TrackChanged_NoOpenEvent_NewTrack_OpensNewEvent()
    {
        var (tl, repo) = Build();
        var userId = UserId();
        var deviceId = Device();
        var newTrack = TrackId();

        await tl.TrackChangedAsync(userId, deviceId, oldTrackId: null, oldPositionMs: 0, newTrackId: newTrack, default);

        repo.CountOpenByUser(userId).Should().Be(1);
        repo.TotalByUser(userId).Should().Be(1);
        repo.GetAll().Single().DeviceId.Should().Be(deviceId);
        repo.GetAll().Single().TrackId.Should().Be(newTrack);
    }

    [Fact]
    public async Task TrackChanged_WithOpenEvent_NewTrack_ClosesOldAndOpensNew()
    {
        var (tl, repo) = Build();
        var userId = UserId();
        var deviceId = Device();
        var trackA = TrackId();
        var trackB = TrackId();

        // Seed an open event for trackA
        var existing = PlayEvent.Begin(userId, deviceId, trackA);
        await repo.AddAsync(existing);

        await tl.TrackChangedAsync(userId, deviceId, oldTrackId: trackA, oldPositionMs: 30_000, newTrackId: trackB, default);

        repo.TotalByUser(userId).Should().Be(2);
        repo.CountClosedByUser(userId).Should().Be(1, "old event must be closed");
        repo.CountOpenByUser(userId).Should().Be(1, "new event must be open");
        repo.GetAll().Single(e => e.EndedAt is null).TrackId.Should().Be(trackB);
        repo.GetAll().Single(e => e.EndedAt is not null).TrackId.Should().Be(trackA);
    }

    [Fact]
    public async Task TrackChanged_WithOpenEvent_NullNewTrack_ClosesOldAndOpensNothing()
    {
        var (tl, repo) = Build();
        var userId = UserId();
        var deviceId = Device();
        var trackA = TrackId();

        var existing = PlayEvent.Begin(userId, deviceId, trackA);
        await repo.AddAsync(existing);

        await tl.TrackChangedAsync(userId, deviceId, oldTrackId: trackA, oldPositionMs: 10_000, newTrackId: null, default);

        repo.TotalByUser(userId).Should().Be(1);
        repo.CountClosedByUser(userId).Should().Be(1, "old event must be closed");
        repo.CountOpenByUser(userId).Should().Be(0, "no new event when next track is null");
    }

    [Fact]
    public async Task TrackChanged_NoOpenEvent_NullNewTrack_DoesNothing()
    {
        var (tl, repo) = Build();
        var userId = UserId();

        await tl.TrackChangedAsync(userId, Device(), oldTrackId: null, oldPositionMs: 0, newTrackId: null, default);

        repo.TotalByUser(userId).Should().Be(0);
    }

    [Fact]
    public async Task TrackChanged_UsesTrackDurationForCompletedRatio()
    {
        // When ITrackRepository resolves the track, Complete uses the real duration.
        var trackId = TrackId();
        var stubRepo = new SingleTrackRepository(trackId, durationMs: 60_000);
        var (tl, repo) = Build(stubRepo);
        var userId = UserId();

        var existing = PlayEvent.Begin(userId, Device(), trackId);
        await repo.AddAsync(existing);

        await tl.TrackChangedAsync(userId, Device(), oldTrackId: trackId, oldPositionMs: 30_000, newTrackId: null, default);

        var closed = repo.GetAll().Single();
        closed.CompletedRatio.Should().BeApproximately(0.5, 0.001,
            "positionMs 30 000 / durationMs 60 000 = 0.5");
    }

    // ── ActiveDeviceChangedAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ActiveDeviceChanged_WithOpenEvent_ClosesOldAndOpensNewForNewDevice()
    {
        var (tl, repo) = Build();
        var userId = UserId();
        var deviceA = Device();
        var deviceB = Device();
        var trackId = TrackId();

        var existing = PlayEvent.Begin(userId, deviceA, trackId);
        await repo.AddAsync(existing);

        await tl.ActiveDeviceChangedAsync(userId, oldDeviceId: deviceA, oldPositionMs: 5_000, newDeviceId: deviceB, default);

        repo.TotalByUser(userId).Should().Be(2);
        repo.CountClosedByUser(userId).Should().Be(1);
        repo.CountOpenByUser(userId).Should().Be(1);

        var newEvent = repo.GetAll().Single(e => e.EndedAt is null);
        newEvent.DeviceId.Should().Be(deviceB);
        newEvent.TrackId.Should().Be(trackId, "same track continues on the new device");
    }

    [Fact]
    public async Task ActiveDeviceChanged_NoOpenEvent_DoesNothing()
    {
        var (tl, repo) = Build();
        var userId = UserId();

        await tl.ActiveDeviceChangedAsync(userId, oldDeviceId: Device(), oldPositionMs: 0, newDeviceId: Device(), default);

        repo.TotalByUser(userId).Should().Be(0);
    }

    [Fact]
    public async Task ActiveDeviceChanged_NullNewDevice_ClosesOldAndOpensNothing()
    {
        var (tl, repo) = Build();
        var userId = UserId();
        var deviceA = Device();

        await repo.AddAsync(PlayEvent.Begin(userId, deviceA, TrackId()));

        await tl.ActiveDeviceChangedAsync(userId, oldDeviceId: deviceA, oldPositionMs: 0, newDeviceId: null, default);

        repo.TotalByUser(userId).Should().Be(1);
        repo.CountClosedByUser(userId).Should().Be(1);
        repo.CountOpenByUser(userId).Should().Be(0);
    }

    // ── SessionClearedAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SessionCleared_WithOpenEvent_ClosesIt()
    {
        var (tl, repo) = Build();
        var userId = UserId();

        await repo.AddAsync(PlayEvent.Begin(userId, Device(), TrackId()));

        await tl.SessionClearedAsync(userId, oldPositionMs: 0, default);

        repo.CountClosedByUser(userId).Should().Be(1);
        repo.CountOpenByUser(userId).Should().Be(0);
    }

    [Fact]
    public async Task SessionCleared_NoOpenEvent_DoesNothing()
    {
        var (tl, repo) = Build();
        var userId = UserId();

        await tl.SessionClearedAsync(userId, oldPositionMs: 0, default);

        repo.TotalByUser(userId).Should().Be(0);
    }

    // ── PausedAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Paused_WithOpenEvent_CallsPauseListeningAndSaves()
    {
        var (tl, repo) = Build();
        var userId = UserId();
        var t0 = DateTimeOffset.UtcNow;

        await repo.AddAsync(PlayEvent.Begin(userId, Device(), TrackId()));
        await tl.PausedAsync(userId, t0, default);

        var ev = repo.GetAll().Single();
        ev.PausedAtUtc.Should().Be(t0, "event must be marked paused at the given time");
        ev.EndedAt.Should().BeNull("pausing does not close the event");
    }

    [Fact]
    public async Task Paused_NoOpenEvent_DoesNothing()
    {
        var (tl, repo) = Build();
        await tl.PausedAsync(UserId(), DateTimeOffset.UtcNow, default);
        repo.TotalByUser(Guid.NewGuid()).Should().Be(0);
    }

    // ── ResumedAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Resumed_WithPausedEvent_ClearsPausedAtUtcAndSaves()
    {
        var (tl, repo) = Build();
        var userId = UserId();
        var t0 = DateTimeOffset.UtcNow;
        var resumeAt = t0.AddMinutes(10);

        var ev = PlayEvent.Begin(userId, Device(), TrackId());
        await repo.AddAsync(ev);
        ev.PauseListening(t0);

        await tl.ResumedAsync(userId, resumeAt, default);

        ev.PausedAtUtc.Should().BeNull("event must be active again after resume");
        ev.SegmentStartedAt.Should().Be(resumeAt);
    }

    [Fact]
    public async Task Resumed_NoOpenEvent_DoesNothing()
    {
        var (tl, repo) = Build();
        await tl.ResumedAsync(UserId(), DateTimeOffset.UtcNow, default);
        // nothing to assert — just must not throw
    }

    // ── stubs ────────────────────────────────────────────────────────────────

    private sealed class NullTrackRepository : ITrackRepository
    {
        public Task<Track?> FindByIdAsync(Guid trackId, CancellationToken ct = default) => Task.FromResult<Track?>(null);
        public Task<Track?> FindByAudioSha1Async(string audioSha1, CancellationToken ct = default) => Task.FromResult<Track?>(null);
        public Task<Artist?> FindArtistByIdAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
        public Task<Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Artist?>(null);
        public Task<Album?> FindAlbumByArtistAndTitleAsync(Guid artistId, string title, CancellationToken ct = default) => Task.FromResult<Album?>(null);
        public Task<Album?> FindAlbumByIdAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult<Album?>(null);
        public Task<int> CountTracksByAlbumIdAsync(Guid albumId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> CountAlbumsByArtistIdAsync(Guid artistId, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddArtistAsync(Artist artist, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddAlbumAsync(Album album, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddTrackAsync(Track track, CancellationToken ct = default) => Task.CompletedTask;
        public void RemoveTrack(Track track) { }
        public void RemoveAlbum(Album album) { }
        public void RemoveArtist(Artist artist) { }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Track>> GetLocalTrackSampleAsync(int maxCount, CancellationToken ct = default) => Task.FromResult(new List<Track>());
    }

    /// <summary>Returns one specific track with a configurable duration; null for everything else.</summary>
    private sealed class SingleTrackRepository(Guid trackId, int durationMs) : ITrackRepository
    {
        private readonly Track _track = Track.Create(
            title: "test",
            albumId: Guid.NewGuid(),
            duration: TimeSpan.FromMilliseconds(durationMs),
            provider: "local",
            format: "flac",
            bitrate: 1000,
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
}
