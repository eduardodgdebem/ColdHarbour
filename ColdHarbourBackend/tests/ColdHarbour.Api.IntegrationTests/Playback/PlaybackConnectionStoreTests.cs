using System.Net.WebSockets;
using ColdHarbour.Api.Playback;
using FluentAssertions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Stress and invariant tests for PlaybackConnectionStore (Phase 2 concurrency fix).
/// The inner set must be ConcurrentDictionary&lt;WebSocket, byte&gt; so every add/remove
/// is atomic — no bag-rebuild races on concurrent connect/disconnect.
/// </summary>
public sealed class PlaybackConnectionStoreTests
{
    // ── invariant 1: 100 parallel connect+disconnect cycles, active sockets survive ──

    [Fact]
    public async Task Parallel_connect_disconnect_cycles_never_lose_an_active_socket()
    {
        var store = new PlaybackConnectionStore();
        var userId = Guid.NewGuid();

        // One "permanent" socket that must survive 100 concurrent connect/disconnect pairs.
        using var permanent = new FakeWebSocket();
        store.Add(userId, permanent);

        const int concurrency = 100;
        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            using var ws = new FakeWebSocket();
            store.Add(userId, ws);
            await Task.Yield(); // force a scheduling gap to amplify races
            store.Remove(userId, ws);
        });

        await Task.WhenAll(tasks);

        // Permanent socket must still receive broadcasts.
        var received = new List<string>();
        permanent.OnSend = msg => received.Add(msg);
        await store.BroadcastAsync(userId, "ping");

        received.Should().ContainSingle(because: "the permanent socket must survive all concurrent add/removes");
    }

    // ── invariant 2: 100 parallel disconnects empty the set completely ──────────────

    [Fact]
    public async Task Parallel_disconnects_on_same_user_empty_the_set()
    {
        var store = new PlaybackConnectionStore();
        var userId = Guid.NewGuid();
        const int count = 100;

        var sockets = Enumerable.Range(0, count).Select(_ => new FakeWebSocket()).ToList();
        foreach (var ws in sockets) store.Add(userId, ws);

        // Remove all in parallel.
        await Task.WhenAll(sockets.Select(ws => Task.Run(() => store.Remove(userId, ws))));

        // No message should arrive at any socket — the set must be empty.
        var received = new List<string>();
        foreach (var ws in sockets) ws.OnSend = msg => received.Add(msg);
        await store.BroadcastAsync(userId, "ping");

        received.Should().BeEmpty(because: "all sockets were removed; broadcast must reach nobody");

        foreach (var ws in sockets) ws.Dispose();
    }

    // ── invariant 3: disconnect racing with new connect does not orphan the new socket ──

    [Fact]
    public async Task Disconnect_racing_with_connect_does_not_orphan_new_socket()
    {
        var store = new PlaybackConnectionStore();
        var userId = Guid.NewGuid();

        // Seed some sockets then trigger a storm: half disconnect, half add new ones.
        const int half = 50;
        var oldSockets = Enumerable.Range(0, half).Select(_ => new FakeWebSocket()).ToList();
        foreach (var ws in oldSockets) store.Add(userId, ws);

        var newSockets = Enumerable.Range(0, half).Select(_ => new FakeWebSocket()).ToList();
        var received = new int[half];
        for (int i = 0; i < half; i++)
        {
            var idx = i;
            newSockets[i].OnSend = _ => Interlocked.Increment(ref received[idx]);
        }

        // Race: old sockets remove themselves while new sockets join.
        var removes = oldSockets.Select(ws => Task.Run(() => store.Remove(userId, ws)));
        var adds = newSockets.Select(ws => Task.Run(() => store.Add(userId, ws)));
        await Task.WhenAll(removes.Concat(adds));

        await store.BroadcastAsync(userId, "ping");

        received.Should().AllSatisfy(r => r.Should().Be(1,
            because: "every new socket added before the broadcast must receive it exactly once"));

        foreach (var ws in oldSockets.Concat(newSockets)) ws.Dispose();
    }

    // ── FakeWebSocket test double ─────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="WebSocket"/> shim. Always in Open state; captures Send calls.
    /// </summary>
    private sealed class FakeWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        public Action<string>? OnSend { get; set; }

        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
        {
            if (_state != WebSocketState.Open)
                throw new WebSocketException(WebSocketError.InvalidState);
            OnSend?.Invoke(System.Text.Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }
}
