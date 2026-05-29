using ColdHarbour.Application.Playback.Ports;
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

    private PostgresPlaybackSessionStore CreateStore() => new(_scopeFactory);

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

    // ── LoadAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_NoSnapshot_ReturnsNull()
    {
        var store = CreateStore();
        var result = await store.LoadAsync(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeNull("no snapshot exists for this user");
    }

    [Fact]
    public async Task LoadAsync_SnapshotExists_ReturnsHydratedSession()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 1);
        session.ClaimActiveIfNone(Guid.NewGuid());
        session.Seek(12_000);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var restored = await store.LoadAsync(userId, CancellationToken.None);

        restored.Should().NotBeNull();
        restored!.Queue.Should().Equal(tracks);
        restored.QueueIndex.Should().Be(1);
        restored.TrackId.Should().Be(tracks[1]);
        restored.PositionMs.Should().Be(12_000);
        restored.IsPlaying.Should().BeTrue();
    }

    // ── SaveAsync — material mutations ───────────────────────────────────────

    [Fact]
    public async Task SaveAsync_Material_WritesSnapshotImmediately()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 2);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

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

        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 0);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        session.Seek(99_000);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        await using var ctx = CreateContext();
        var count = await ctx.PlaybackSessionSnapshots.CountAsync(s => s.UserId == userId);
        count.Should().Be(1);

        var snap = await ctx.PlaybackSessionSnapshots.FindAsync(userId);
        snap!.PositionMs.Should().Be(99_000);
    }

    // ── SaveAsync — HeartbeatThrottled writes unconditionally (throttle is actor-side) ──

    [Fact]
    public async Task SaveAsync_HeartbeatThrottled_WritesImmediately()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 0);
        session.Seek(1_000);
        await store.SaveAsync(session, SaveReason.HeartbeatThrottled, CancellationToken.None);

        await using var ctx = CreateContext();
        var snap = await ctx.PlaybackSessionSnapshots.FindAsync(userId);
        snap.Should().NotBeNull("HeartbeatThrottled writes are unconditional at the store level");
        snap!.PositionMs.Should().Be(1_000);
    }

    // ── Round-trip: all fields ───────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_AllFields_PreservedAcrossLoad()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var activeDevice = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 1);
        session.ClaimActiveIfNone(activeDevice);
        session.Seek(55_000);
        session.SetRepeatMode(RepeatMode.All);
        session.SetShuffle(true);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var restored = await store.LoadAsync(userId, CancellationToken.None);

        restored.Should().NotBeNull();
        restored!.UserId.Should().Be(userId);
        restored.ActiveDeviceId.Should().Be(activeDevice);
        restored.TrackId.Should().Be(tracks[1]);
        restored.PositionMs.Should().Be(55_000);
        restored.IsPlaying.Should().BeTrue();
        restored.Queue.Should().Equal(tracks);
        restored.QueueIndex.Should().Be(1);
        restored.RepeatMode.Should().Be(RepeatMode.All);
        restored.Shuffle.Should().BeTrue();
    }

    // ── Phase 4: Revision round-trip ─────────────────────────────────────────

    [Fact]
    public async Task Revision_Survives_Round_Trip_Via_Postgres()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        var session = PlaybackSession.Create(userId);
        session.IncrementRevision();
        session.IncrementRevision(); // revision == 2

        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var restored = await store.LoadAsync(userId, CancellationToken.None);
        restored!.Revision.Should().Be(2, "revision must survive a Postgres save/load round-trip");
    }

    // ── Clone semantics: each LoadAsync returns an independent snapshot ───────

    [Fact]
    public async Task LoadAsync_ReturnsFreshInstance_EachTime()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        var session = PlaybackSession.Create(userId);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var a = await store.LoadAsync(userId, CancellationToken.None);
        var b = await store.LoadAsync(userId, CancellationToken.None);

        a.Should().NotBeSameAs(b, "each LoadAsync deserialises a fresh instance from the DB");
    }
}
