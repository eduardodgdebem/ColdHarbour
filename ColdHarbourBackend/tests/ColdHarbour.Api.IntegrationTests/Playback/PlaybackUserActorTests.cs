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
        var session = await store.LoadAsync(userId) ?? PlaybackSession.Create(userId);

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

        var session = await sp.GetRequiredService<IPlaybackSessionStore>().LoadAsync(userId) ?? PlaybackSession.Create(userId);
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

    // ── Phase 4: revision monotonicity ───────────────────────────────────────

    [Fact]
    public async Task Actor_Increments_Revision_On_Each_Material_Change()
    {
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await using var actor = BuildActor(sp, userId);
        await actor.EnqueueAsync(new SetQueueCmd(device, tracks, 0), CancellationToken.None);
        await actor.DisposeAsync();

        var store = sp.GetRequiredService<IPlaybackSessionStore>();
        var session = await store.LoadAsync(userId) ?? PlaybackSession.Create(userId);
        session.Revision.Should().Be(1, "one material command must produce revision 1");
    }

    [Fact]
    public async Task Revision_Is_Strictly_Monotonic_Under_1000_Concurrent_Commands()
    {
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        await using var actor = BuildActor(sp, userId);

        var tasks = Enumerable.Range(0, 1000)
            .Select(_ => actor.EnqueueAsync(new SetQueueCmd(device, tracks, 0), CancellationToken.None).AsTask());
        await Task.WhenAll(tasks);
        await actor.DisposeAsync();

        var store = sp.GetRequiredService<IPlaybackSessionStore>();
        var session = await store.LoadAsync(userId) ?? PlaybackSession.Create(userId);
        session.Revision.Should().Be(1000,
            "each of the 1000 material commands must increment revision exactly once");
    }

    // ── Phase 4: command-ack unicast ──────────────────────────────────────────

    [Fact]
    public async Task CommandId_Applied_Ack_Is_Unicast_To_Source_Socket()
    {
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        using var fakeWs = new FakeWebSocket();
        await using var actor = BuildActor(sp, userId);

        await actor.EnqueueAsync(
            new SetQueueCmd(device, tracks, 0),
            commandId: "cmd-abc-123",
            source: fakeWs,
            CancellationToken.None);

        await actor.DisposeAsync();

        fakeWs.ReceivedMessages.Should().ContainSingle();
        var ack = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            fakeWs.ReceivedMessages[0]);
        ack!["type"]!.GetValue<string>().Should().Be("command-ack");
        ack["commandId"]!.GetValue<string>().Should().Be("cmd-abc-123");
        ack["status"]!.GetValue<string>().Should().Be("applied");
        ack["revision"]!.GetValue<long>().Should().Be(1);
    }

    [Fact]
    public async Task CommandId_Duplicate_Produces_Noop_Ack_Not_Applied()
    {
        var sp = BuildServices();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };
        const string cmdId = "dedup-cmd-999";

        using var fakeWs = new FakeWebSocket();
        await using var actor = BuildActor(sp, userId);

        // First send — must be applied.
        await actor.EnqueueAsync(new SetQueueCmd(device, tracks, 0), cmdId, fakeWs, CancellationToken.None);
        // Second send with same commandId — must be noop.
        await actor.EnqueueAsync(new SetQueueCmd(device, tracks, 0), cmdId, fakeWs, CancellationToken.None);
        await actor.DisposeAsync();

        var acks = fakeWs.ReceivedMessages
            .Select(m => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(m)!)
            .Where(j => j["type"]?.GetValue<string>() == "command-ack")
            .ToList();

        acks.Should().HaveCount(2);
        acks.Count(a => a["status"]!.GetValue<string>() == "applied").Should().Be(1,
            "a duplicate commandId must produce exactly one applied ack");
        acks.Count(a => a["status"]!.GetValue<string>() == "noop").Should().Be(1,
            "the duplicate must produce exactly one noop ack");
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

    /// <summary>
    /// Open fake WebSocket that captures every text frame sent to it.
    /// Used to assert command-ack unicast payloads.
    /// </summary>
    private sealed class FakeWebSocket : System.Net.WebSockets.WebSocket
    {
        private bool _closed;
        public List<string> ReceivedMessages { get; } = [];

        public override System.Net.WebSockets.WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override System.Net.WebSockets.WebSocketState State =>
            _closed ? System.Net.WebSockets.WebSocketState.Closed : System.Net.WebSockets.WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort() => _closed = true;
        public override Task CloseAsync(System.Net.WebSockets.WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct) { _closed = true; return Task.CompletedTask; }
        public override Task CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct) { _closed = true; return Task.CompletedTask; }
        public override void Dispose() => _closed = true;

        public override Task<System.Net.WebSockets.WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            => Task.FromResult(new System.Net.WebSockets.WebSocketReceiveResult(0, System.Net.WebSockets.WebSocketMessageType.Close, true));

        public override Task SendAsync(ArraySegment<byte> buffer, System.Net.WebSockets.WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
        {
            if (messageType == System.Net.WebSockets.WebSocketMessageType.Text)
                ReceivedMessages.Add(System.Text.Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }
}
