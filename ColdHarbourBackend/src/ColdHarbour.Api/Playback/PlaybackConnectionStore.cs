using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace ColdHarbour.Api.Playback;

/// <summary>
/// Singleton registry of active WebSocket connections keyed by userId.
/// Extracted from PlaybackSessionHub so the actor can broadcast without
/// coupling to the hub's static state.
/// Note: Phase 2 replaces ConcurrentBag with ConcurrentDictionary&lt;WebSocket, byte&gt;
/// to close the bag-rebuild race on concurrent connect/disconnect.
/// </summary>
public sealed class PlaybackConnectionStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<WebSocket>> _connections = new();

    public void Add(Guid userId, WebSocket ws)
        => _connections.GetOrAdd(userId, _ => []).Add(ws);

    public void Remove(Guid userId, WebSocket ws)
    {
        if (!_connections.TryGetValue(userId, out var bag)) return;
        var remaining = bag.Where(s => s != ws).ToList();
        _connections.TryUpdate(userId, new ConcurrentBag<WebSocket>(remaining), bag);
    }

    public async Task BroadcastAsync(Guid userId, string jsonPayload)
    {
        if (!_connections.TryGetValue(userId, out var sockets)) return;

        var bytes = Encoding.UTF8.GetBytes(jsonPayload);
        var segment = new ArraySegment<byte>(bytes);
        var dead = new List<WebSocket>();

        foreach (var ws in sockets)
        {
            if (ws.State != WebSocketState.Open) { dead.Add(ws); continue; }
            try { await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None); }
            catch { dead.Add(ws); }
        }

        if (dead.Count > 0)
        {
            var remaining = sockets.Except(dead).ToList();
            _connections.TryUpdate(userId, new ConcurrentBag<WebSocket>(remaining), sockets);
        }
    }
}
