using System.Reflection;
using ColdHarbour.Api.Playback;
using ColdHarbour.Application;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Playback hardening Phase 1. Two behaviours on the per-user actor:
///   (A) a session owned by a device that has gone away (no live socket + LastSeenAt past the
///       liveness TTL) is released — ActiveDeviceId demoted to null and broadcast.
///   (B) idle heartbeats (paused or no track) are dropped server-side, defence-in-depth behind
///       the frontend gate.
/// Drives the actor through the real AddApplication() pipeline.
/// </summary>
public sealed class PlaybackUserActorLivenessTests
{
    private static ServiceProvider BuildServices(
        FakeConnectedDeviceStore connected,
        FakeDeviceRepository devices,
        int ttlSeconds = 30)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton(new PlaybackLimits { ActiveDeviceTtlSeconds = ttlSeconds });
        services.AddSingleton<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>();
        services.AddSingleton<IPlaybackSessionStore>(sp =>
            sp.GetRequiredService<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>());
        services.AddSingleton<PlaybackConnectionStore>();
        services.AddScoped<IPlayEventRepository, NoopPlayEventRepository>();
        services.AddScoped<ITrackRepository, NoopTrackRepository>();
        services.AddSingleton<IConnectedDeviceStore>(connected);
        services.AddScoped<IDeviceRepository>(_ => devices);
        return services.BuildServiceProvider();
    }

    private static PlaybackUserActor BuildActor(IServiceProvider sp, Guid userId) => new(
        userId,
        sp.GetRequiredService<IPlaybackSessionStore>(),
        sp.GetRequiredService<PlaybackConnectionStore>(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<PlaybackUserActor>.Instance);

    private static async Task SeedActiveSession(
        IServiceProvider sp, Guid userId, Guid device, Guid track, bool playing = true)
    {
        var store = sp.GetRequiredService<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>();
        var s = PlaybackSession.Create(userId);
        s.SetQueue(new[] { track }, 0);
        s.ClaimActiveIfNone(device);
        if (!playing) s.Pause();
        await store.SaveAsync(s, SaveReason.MaterialChange);
    }

    // ── (A) stale active-device demotion ──────────────────────────────────────

    [Fact]
    public async Task CheckLiveness_DemotesActiveDevice_WhenSocketGoneAndLastSeenPastTtl()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore();          // device NOT connected
        var devices = new FakeDeviceRepository();
        devices.Add(device, userId, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-5)); // stale
        var sp = BuildServices(connected, devices, ttlSeconds: 30);

        await SeedActiveSession(sp, userId, device, Guid.NewGuid());

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueLivenessCheckAsync(CancellationToken.None);
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.ActiveDeviceId.Should().BeNull("a dead owner past the TTL is released");
        session.TrackId.Should().NotBeNull("playback context is preserved for the next device");
    }

    [Fact]
    public async Task CheckLiveness_KeepsActiveDevice_WhenStillConnected()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore(device);    // device IS connected
        var devices = new FakeDeviceRepository();
        devices.Add(device, userId, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-5));
        var sp = BuildServices(connected, devices);

        await SeedActiveSession(sp, userId, device, Guid.NewGuid());

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueLivenessCheckAsync(CancellationToken.None);
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.ActiveDeviceId.Should().Be(device, "a connected device keeps ownership regardless of LastSeenAt");
    }

    [Fact]
    public async Task CheckLiveness_KeepsActiveDevice_WhenRecentlySeen()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore();          // not connected
        var devices = new FakeDeviceRepository();
        devices.Add(device, userId, lastSeen: DateTimeOffset.UtcNow);  // seen just now
        var sp = BuildServices(connected, devices, ttlSeconds: 30);

        await SeedActiveSession(sp, userId, device, Guid.NewGuid());

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueLivenessCheckAsync(CancellationToken.None);
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.ActiveDeviceId.Should().Be(device, "a device seen within the TTL keeps ownership");
    }

    [Fact]
    public async Task CheckLiveness_KeepsActiveDevice_WhenDeviceUnknown()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore();          // not connected
        var devices = new FakeDeviceRepository();                // device record absent
        var sp = BuildServices(connected, devices, ttlSeconds: 30);

        await SeedActiveSession(sp, userId, device, Guid.NewGuid());

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueLivenessCheckAsync(CancellationToken.None);
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.ActiveDeviceId.Should().Be(device, "an unknown device is not positive evidence of staleness");
    }

    [Fact]
    public async Task Transport_FromAnotherDevice_RecoversStaleSession()
    {
        // The recovery path: a stale owner is demoted ahead of the command, then the sender claims active.
        var userId = Guid.NewGuid();
        var deadDevice = Guid.NewGuid();
        var liveDevice = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore(liveDevice);   // only the live device is connected
        var devices = new FakeDeviceRepository();
        devices.Add(deadDevice, userId, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-5));
        devices.Add(liveDevice, userId, lastSeen: DateTimeOffset.UtcNow);
        var sp = BuildServices(connected, devices, ttlSeconds: 30);

        await SeedActiveSession(sp, userId, deadDevice, Guid.NewGuid());

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new PauseCmd(liveDevice), CancellationToken.None);
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.ActiveDeviceId.Should().Be(liveDevice, "the live device takes over the released session");
    }

    // ── (B) heartbeat gating ──────────────────────────────────────────────────

    [Fact]
    public async Task Heartbeat_Dropped_WhenSessionPaused()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore(device);
        var devices = new FakeDeviceRepository();
        devices.Add(device, userId, lastSeen: DateTimeOffset.UtcNow);
        var sp = BuildServices(connected, devices);

        await SeedActiveSession(sp, userId, device, Guid.NewGuid(), playing: false);

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new HeartbeatCmd(device, 9_000), CancellationToken.None);
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.PositionMs.Should().Be(0, "an idle (paused) heartbeat must be dropped");
        session.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task Heartbeat_Applied_WhenPlaying()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore(device);
        var devices = new FakeDeviceRepository();
        devices.Add(device, userId, lastSeen: DateTimeOffset.UtcNow);
        var sp = BuildServices(connected, devices);

        await SeedActiveSession(sp, userId, device, Guid.NewGuid(), playing: true);

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new HeartbeatCmd(device, 4_000), CancellationToken.None);
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.PositionMs.Should().Be(4_000, "a playing heartbeat within the drift bound updates the position");
    }

    [Fact]
    public async Task Heartbeat_Dropped_WhenBeyondDriftBound()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var connected = new FakeConnectedDeviceStore(device);
        var devices = new FakeDeviceRepository();
        devices.Add(device, userId, lastSeen: DateTimeOffset.UtcNow);
        var sp = BuildServices(connected, devices);

        await SeedActiveSession(sp, userId, device, Guid.NewGuid(), playing: true); // PositionMs = 0

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new HeartbeatCmd(device, 50_000), CancellationToken.None); // teleport
        await actor.DisposeAsync();

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId);
        session!.PositionMs.Should().Be(0, "a heartbeat past the drift bound is dropped");
    }

    // ── doubles ───────────────────────────────────────────────────────────────

    private sealed class FakeConnectedDeviceStore(params Guid[] connected) : IConnectedDeviceStore
    {
        private readonly HashSet<Guid> _connected = new(connected);
        public void Add(Guid deviceId) => _connected.Add(deviceId);
        public void Remove(Guid deviceId) => _connected.Remove(deviceId);
        public IReadOnlySet<Guid> GetConnected() => _connected;
    }

    private sealed class FakeDeviceRepository : IDeviceRepository
    {
        private readonly Dictionary<Guid, Device> _devices = new();

        public void Add(Guid id, Guid userId, DateTimeOffset lastSeen)
        {
            var device = Device.Register(id, userId, "Test", "UA", ["mp3"], "opus-128");
            // LastSeenAt has a private setter; reach it for the test so we can age the record.
            typeof(Device).GetProperty(nameof(Device.LastSeenAt))!
                .GetSetMethod(nonPublic: true)!.Invoke(device, [lastSeen]);
            _devices[id] = device;
        }

        public Task<Device?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_devices.GetValueOrDefault(id));
        public Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct = default)
            => Task.FromResult(_devices.ContainsKey(deviceId));
        public Task<IReadOnlyList<Device>> ListByUserIdAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Device>>(_devices.Values.ToList());
        public Task AddAsync(Device device, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class NoopPlayEventRepository : IPlayEventRepository
    {
        public Task AddAsync(PlayEvent e, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<PlayEvent?>(null);
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
}
