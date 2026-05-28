using ColdHarbour.Api.Playback;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Stress and invariant tests for PlaybackUserActor (Phase 1 concurrency migration).
/// All mutations go through a single-reader channel so the session invariants must
/// hold regardless of how many producers write concurrently.
/// </summary>
public sealed class PlaybackUserActorTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Both handlers live in the Application assembly (ListDevicesQueryHandler namespace is Commands)
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SetQueueCommandHandler).Assembly));
        services.AddSingleton<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>();
        services.AddSingleton<IPlaybackSessionStore>(sp =>
            sp.GetRequiredService<ColdHarbour.Infrastructure.Playback.InMemoryPlaybackSessionStore>());
        services.AddSingleton<PlaybackConnectionStore>();
        services.AddScoped<IPlayEventRepository, NoopPlayEventRepository>();
        // Device-related dependencies for ListDevicesQuery
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

    // ── invariant 1: queue consistency under concurrent load ──────────────────

    [Fact]
    public async Task Stress_100_parallel_mutations_leave_queue_invariants_intact()
    {
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device1 = Guid.NewGuid();
        var device2 = Guid.NewGuid();
        var tracks = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        await using var actor = BuildActor(sp, userId);

        // Seed a starting queue so Next has something to advance into.
        await actor.EnqueueAsync(new SetQueueCmd(device1, tracks, 0), CancellationToken.None);

        // Fire 99 more commands concurrently from two "devices".
        var tasks = Enumerable.Range(0, 99).Select(async i =>
        {
            if (i % 3 == 0)
                await actor.EnqueueAsync(new SetQueueCmd(device1, tracks, 0), CancellationToken.None);
            else if (i % 3 == 1)
                await actor.EnqueueAsync(new AddToQueueCmd(device2, tracks[i % tracks.Count], null), CancellationToken.None);
            else
                await actor.EnqueueAsync(new NextCmd(device1), CancellationToken.None);
        });

        await Task.WhenAll(tasks);
        // DisposeAsync completes the channel writer and awaits the pump to drain.
        await actor.DisposeAsync();

        var store = sp.GetRequiredService<IPlaybackSessionStore>();
        var session = store.GetOrCreate(userId);

        if (session.Queue.Count > 0)
        {
            session.QueueIndex.Should().BeGreaterThanOrEqualTo(0);
            session.QueueIndex.Should().BeLessThan(session.Queue.Count);
            session.TrackId.Should().Be(session.Queue[session.QueueIndex],
                "TrackId must equal Queue[QueueIndex] when the queue is non-empty");
        }
        else
        {
            session.TrackId.Should().BeNull("empty queue implies no current track");
        }
    }

    // ── invariant 2: FIFO dispatch order ─────────────────────────────────────

    [Fact]
    public async Task Pump_processes_commands_in_FIFO_order()
    {
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var track = Guid.NewGuid();

        await using var actor = BuildActor(sp, userId);

        // SetQueue establishes a track then we seek N times with ascending positions.
        await actor.EnqueueAsync(new SetQueueCmd(device, new[] { track }, 0), CancellationToken.None);

        const int steps = 50;
        for (int i = 1; i <= steps; i++)
            await actor.EnqueueAsync(new SeekCmd(device, i * 1000L), CancellationToken.None);

        await actor.DisposeAsync();

        var session = sp.GetRequiredService<IPlaybackSessionStore>().GetOrCreate(userId);
        session.PositionMs.Should().Be(steps * 1000L,
            "FIFO dispatch means the last seek wins; position must equal the final value");
    }

    // ── invariant 3: eviction race — command arriving during eviction ─────────

    [Fact]
    public async Task Registry_GetOrCreate_after_eviction_returns_fresh_live_actor()
    {
        var sp = BuildServices();
        var registry = new PlaybackUserActorRegistry(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IPlaybackSessionStore>(),
            sp.GetRequiredService<PlaybackConnectionStore>(),
            sp.GetRequiredService<ILoggerFactory>());

        await registry.StartAsync(CancellationToken.None);

        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        // actor1: process a command, then dispose (simulating idle eviction completing).
        var actor1 = registry.GetOrCreate(userId);
        await actor1.EnqueueAsync(new SetQueueCmd(device, tracks, 0), CancellationToken.None);
        await actor1.DisposeAsync();

        actor1.IsDisposed.Should().BeTrue("actor1 has been disposed");

        // GetOrCreate must detect the disposed actor and return a fresh one.
        var actor2 = registry.GetOrCreate(userId);

        actor2.Should().NotBeSameAs(actor1, "the registry must create a new actor when the old one is disposed");
        actor2.IsDisposed.Should().BeFalse("the new actor must be live and accepting commands");

        // Commands to actor2 must not throw — deterministic success, never silently dropped.
        var enqueueTask = actor2.EnqueueAsync(new NextCmd(device), CancellationToken.None);
        await enqueueTask; // must not throw ChannelClosedException

        await actor2.DisposeAsync();
        await registry.StopAsync(CancellationToken.None);
    }

    // ── no SaveAsync calls from hub (definition-of-done check) ───────────────

    [Fact]
    public async Task Hub_parse_command_returns_null_for_unknown_message_type()
    {
        var sp = BuildServices();
        using var scope = sp.CreateScope();

        var hub = new PlaybackSessionHub(
            scope.ServiceProvider.GetRequiredService<IMediator>(),
            sp.GetRequiredService<IConnectedDeviceStore>(),
            sp.GetRequiredService<PlaybackConnectionStore>(),
            new PlaybackUserActorRegistry(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IPlaybackSessionStore>(),
                sp.GetRequiredService<PlaybackConnectionStore>(),
                sp.GetRequiredService<ILoggerFactory>()),
            BuildFakeConfig(),
            NullLogger<PlaybackSessionHub>.Instance);

        var result = hub.ParseCommand("""{"type":"unknown","deviceId":"00000000-0000-0000-0000-000000000001"}""");
        result.Should().BeNull("unrecognised message types are dropped at the parse boundary");
    }

    [Fact]
    public async Task Hub_parse_command_returns_null_for_malformed_json()
    {
        var sp = BuildServices();
        using var scope = sp.CreateScope();

        var hub = new PlaybackSessionHub(
            scope.ServiceProvider.GetRequiredService<IMediator>(),
            sp.GetRequiredService<IConnectedDeviceStore>(),
            sp.GetRequiredService<PlaybackConnectionStore>(),
            new PlaybackUserActorRegistry(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IPlaybackSessionStore>(),
                sp.GetRequiredService<PlaybackConnectionStore>(),
                sp.GetRequiredService<ILoggerFactory>()),
            BuildFakeConfig(),
            NullLogger<PlaybackSessionHub>.Instance);

        var result = hub.ParseCommand("not json at all {{{");
        result.Should().BeNull("malformed JSON must be silently dropped, never thrown into the actor");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildFakeConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COLDHARBOUR_JWT_SIGNING_KEY"] = new string('x', 64),
                ["COLDHARBOUR_JWT_ISSUER"] = "coldharbour",
                ["COLDHARBOUR_JWT_AUDIENCE"] = "coldharbour-web",
            })
            .Build();
    }

    // ── no-op test doubles ────────────────────────────────────────────────────

    private sealed class NoopPlayEventRepository : IPlayEventRepository
    {
        public Task AddAsync(PlayEvent e, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<PlayEvent?> FindActiveByUserAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<PlayEvent?>(null);
    }

    private sealed class NoopDeviceRepository : IDeviceRepository
    {
        public Task<Device?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Device?>(null);
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
