using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Persistence;
using ColdHarbour.Infrastructure.Playback;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Playback;

/// <summary>
/// Integration tests for <see cref="PostgresPlaybackSessionStore"/>.
/// A single Postgres container is shared across the class; each test gets a
/// fresh store instance (and the container's DB is migrated once in
/// <see cref="InitializeAsync"/>).
/// </summary>
public sealed class PostgresPlaybackSessionStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private IServiceScopeFactory _scopeFactory = null!;

    // Helper to create a fresh store, optionally with a controllable clock.
    private PostgresPlaybackSessionStore CreateStore(Func<DateTimeOffset>? clock = null) =>
        new(_scopeFactory, clock);

    // Helper to get a context for direct DB assertions.
    private ColdHarbourDbContext CreateContext()
    {
        var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ColdHarbourDbContext>();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ColdHarbourDbContext>(opts =>
            opts.UseNpgsql(_postgres.GetConnectionString()));
        var provider = services.BuildServiceProvider();
        _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdHarbourDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // -----------------------------------------------------------------------
    // GetOrCreate
    // -----------------------------------------------------------------------

    [Fact]
    public void GetOrCreate_NoSnapshot_ReturnsFreshIdleSession()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        var session = store.GetOrCreate(userId);

        session.UserId.Should().Be(userId);
        session.TrackId.Should().BeNull();
        session.IsPlaying.Should().BeFalse();
        session.Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreate_SnapshotExists_ReturnsHydratedSession()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        // Seed a session and persist it.
        var session = store.GetOrCreate(userId);
        session.SetQueue(tracks, 1);
        session.ClaimActiveIfNone(Guid.NewGuid());
        session.Seek(12_000);
        await store.SaveAsync(session, isHeartbeat: false);

        // A fresh store instance must hydrate from the DB via StartAsync.
        var freshStore = CreateStore();
        await freshStore.StartAsync(CancellationToken.None);

        var restored = freshStore.GetOrCreate(userId);

        restored.Queue.Should().Equal(tracks);
        restored.QueueIndex.Should().Be(1);
        restored.TrackId.Should().Be(tracks[1]);
        restored.PositionMs.Should().Be(12_000);
        restored.IsPlaying.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SaveAsync — material mutations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_Material_WritesSnapshotImmediately()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var session = store.GetOrCreate(userId);
        session.SetQueue(tracks, 2);
        await store.SaveAsync(session, isHeartbeat: false);

        // Read snapshot directly from DB.
        await using var ctx = CreateContext();
        var snap = await ctx.PlaybackSessionSnapshots.FindAsync(userId);

        snap.Should().NotBeNull();
        snap!.QueueIndex.Should().Be(2);
        snap.TrackId.Should().Be(tracks[2]);
        var snappedQueue = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(snap.QueueJson);
        snappedQueue.Should().Equal(tracks);
    }

    [Fact]
    public async Task SaveAsync_Material_IsIdempotent()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        var session = store.GetOrCreate(userId);
        session.SetQueue(tracks, 0);
        await store.SaveAsync(session, isHeartbeat: false);

        // Mutate further and save again — must upsert, not duplicate.
        session.Seek(99_000);
        await store.SaveAsync(session, isHeartbeat: false);

        await using var ctx = CreateContext();
        var count = await ctx.PlaybackSessionSnapshots.CountAsync(s => s.UserId == userId);
        count.Should().Be(1);

        var snap = await ctx.PlaybackSessionSnapshots.FindAsync(userId);
        snap!.PositionMs.Should().Be(99_000);
    }

    // -----------------------------------------------------------------------
    // SaveAsync — heartbeat throttle
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_Heartbeat_FirstWrite_Persists()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        var session = store.GetOrCreate(userId);
        session.SetQueue(tracks, 0);

        // First heartbeat: must write.
        session.Seek(1_000);
        await store.SaveAsync(session, isHeartbeat: true);

        await using var ctx = CreateContext();
        var snap = await ctx.PlaybackSessionSnapshots.FindAsync(userId);
        snap.Should().NotBeNull("first heartbeat must be persisted");
        snap!.PositionMs.Should().Be(1_000);
    }

    [Fact]
    public async Task SaveAsync_Heartbeat_SecondWriteWithin5s_IsSkipped()
    {
        var now = DateTimeOffset.UtcNow;
        Func<DateTimeOffset> clock = () => now;

        var store = CreateStore(clock);
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        var session = store.GetOrCreate(userId);
        session.SetQueue(tracks, 0);

        // First heartbeat at T=0 — writes.
        session.Seek(1_000);
        await store.SaveAsync(session, isHeartbeat: true);

        // Second heartbeat at T=1s — must be throttled.
        now = now.AddSeconds(1);
        session.Seek(3_000);
        await store.SaveAsync(session, isHeartbeat: true);

        await using var ctx = CreateContext();
        var snap = await ctx.PlaybackSessionSnapshots.FindAsync(userId);
        snap!.PositionMs.Should().Be(1_000, "throttled write must not overwrite the persisted value");
    }

    [Fact]
    public async Task SaveAsync_Heartbeat_AfterThrottleWindow_Writes()
    {
        var now = DateTimeOffset.UtcNow;
        Func<DateTimeOffset> clock = () => now;

        var store = CreateStore(clock);
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        var session = store.GetOrCreate(userId);
        session.SetQueue(tracks, 0);

        // First heartbeat at T=0.
        session.Seek(1_000);
        await store.SaveAsync(session, isHeartbeat: true);

        // Second heartbeat at T=6s — past the 5 s window, must write.
        now = now.AddSeconds(6);
        session.Seek(7_000);
        await store.SaveAsync(session, isHeartbeat: true);

        await using var ctx = CreateContext();
        var snap = await ctx.PlaybackSessionSnapshots.FindAsync(userId);
        snap!.PositionMs.Should().Be(7_000, "heartbeat after throttle window must persist");
    }

    // -----------------------------------------------------------------------
    // Round-trip: all fields
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RoundTrip_AllFields_PreservedAfterRestart()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var activeDevice = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var session = store.GetOrCreate(userId);
        session.SetQueue(tracks, 1);
        session.ClaimActiveIfNone(activeDevice);
        session.Seek(55_000);
        session.SetRepeatMode(RepeatMode.All);
        session.SetShuffle(true);
        await store.SaveAsync(session, isHeartbeat: false);

        // Fresh store simulates api restart.
        var freshStore = CreateStore();
        await freshStore.StartAsync(CancellationToken.None);
        var restored = freshStore.GetOrCreate(userId);

        restored.UserId.Should().Be(userId);
        restored.ActiveDeviceId.Should().Be(activeDevice);
        restored.TrackId.Should().Be(tracks[1]);
        restored.PositionMs.Should().Be(55_000);
        restored.IsPlaying.Should().BeTrue();
        restored.Queue.Should().Equal(tracks);
        restored.QueueIndex.Should().Be(1);
        restored.RepeatMode.Should().Be(RepeatMode.All);
        restored.Shuffle.Should().BeTrue();
    }
}
