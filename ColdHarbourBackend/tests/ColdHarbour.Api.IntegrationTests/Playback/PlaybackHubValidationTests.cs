using ColdHarbour.Api.Playback;
using ColdHarbour.Application;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Phase 5 of WS_PROTOCOL_HARDENING. Drives the per-user actor through the *real* Application
/// pipeline (AddApplication wires ValidationBehavior + the playback validators + PlaybackLimits),
/// so an invalid hub-dispatched command throws FluentValidation.ValidationException inside the
/// pipeline. The actor must drop that message and leave the session untouched — never close the
/// socket, never mutate state.
/// </summary>
public sealed class PlaybackHubValidationTests
{
    private static ServiceProvider BuildServices(int maxQueueSize = 1000)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(); // MediatR + validators + ValidationBehavior + IPlaySessionTimeline + PlaybackLimits

        // Override PlaybackLimits the same way the composition root does.
        services.AddSingleton(new ColdHarbour.Application.Playback.PlaybackLimits { MaxQueueSize = maxQueueSize });

        services.AddSingleton<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>();
        services.AddSingleton<IPlaybackSessionStore>(sp =>
            sp.GetRequiredService<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>());
        services.AddSingleton<PlaybackConnectionStore>();
        services.AddScoped<IPlayEventRepository, NoopPlayEventRepository>();
        services.AddScoped<ITrackRepository, NoopTrackRepository>();
        services.AddScoped<IDeviceRepository, NoopDeviceRepository>();
        services.AddSingleton<IConnectedDeviceStore, NoopConnectedDeviceStore>();
        return services.BuildServiceProvider();
    }

    private static PlaybackUserActor BuildActor(IServiceProvider sp, Guid userId) => new(
        userId,
        sp.GetRequiredService<IPlaybackSessionStore>(),
        sp.GetRequiredService<PlaybackConnectionStore>(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<PlaybackUserActor>.Instance);

    [Fact]
    public async Task SetQueue_over_max_size_is_dropped_session_unchanged()
    {
        var sp = BuildServices(maxQueueSize: 1000);
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = Enumerable.Range(0, 5000).Select(_ => Guid.NewGuid()).ToList();

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new SetQueueCmd(device, tracks, 0), CancellationToken.None);
        await actor.DisposeAsync();

        var store = sp.GetRequiredService<IPlaybackSessionStore>();
        var session = await store.LoadAsync(userId);
        // The validator rejected the command before the handler ran: no session was ever persisted.
        (session is null || session.Queue.Count == 0).Should().BeTrue(
            "a setQueue over COLDHARBOUR_WS_MAX_QUEUE_SIZE must be dropped, leaving the session unchanged");
    }

    [Fact]
    public async Task Seek_with_negative_position_is_dropped_session_unchanged()
    {
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();

        // Seed a real session: a track is loaded, owned by device, at position 5000.
        var store = sp.GetRequiredService<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>();
        var seeded = PlaybackSession.Create(userId);
        seeded.SetQueue(new[] { track }, 0);
        seeded.ClaimActiveIfNone(device);
        seeded.UpdatePosition(5_000);
        await store.SaveAsync(seeded, SaveReason.MaterialChange);

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new SeekCmd(device, -1), CancellationToken.None);
        await actor.DisposeAsync();

        var session = await store.LoadAsync(userId);
        session!.PositionMs.Should().Be(5_000,
            "a seek with a negative positionMs must be dropped, leaving the last position intact");
    }

    [Fact]
    public async Task Valid_seek_still_applies()
    {
        // Guard against a validator that rejects everything: a legal command must still pass.
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();

        var store = sp.GetRequiredService<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>();
        var seeded = PlaybackSession.Create(userId);
        seeded.SetQueue(new[] { track }, 0);
        seeded.ClaimActiveIfNone(device);
        await store.SaveAsync(seeded, SaveReason.MaterialChange);

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new SeekCmd(device, 12_000), CancellationToken.None);
        await actor.DisposeAsync();

        var session = await store.LoadAsync(userId);
        session!.PositionMs.Should().Be(12_000, "a valid seek must be applied normally");
    }

    // ── no-op test doubles ────────────────────────────────────────────────────

    private sealed class NoopPlayEventRepository : IPlayEventRepository
    {
        public Task AddAsync(PlayEvent e, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<PlayEvent?>(null);
        public Task<IReadOnlyList<PlayEvent>> FindOrphanedAsync(DateTimeOffset before, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PlayEvent>>(Array.Empty<PlayEvent>());
    }

    private sealed class NoopTrackRepository : ITrackRepository
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

    private sealed class NoopDeviceRepository : IDeviceRepository
    {
        public Task<Device?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Device?>(null);
        public Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<Device>> ListByUserIdAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Device>>(Array.Empty<Device>());
        public Task AddAsync(Device device, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class NoopConnectedDeviceStore : IConnectedDeviceStore
    {
        public void Add(Guid deviceId) { }
        public void Remove(Guid deviceId) { }
        public IReadOnlySet<Guid> GetConnected() => new HashSet<Guid>();
    }
}
