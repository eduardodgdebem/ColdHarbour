using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace ColdHarbour.Api.Playback;

/// <summary>
/// Singleton registry of active WebSocket connections keyed by userId.
/// Uses ConcurrentDictionary&lt;WebSocket, byte&gt; as a set so every add/remove is
/// atomic — no bag-rebuild race on concurrent connect/disconnect (Phase 2 fix).
/// </summary>
public sealed class PlaybackConnectionStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<WebSocket, byte>> _connections = new();

    public void Add(Guid userId, WebSocket ws)
        => _connections.GetOrAdd(userId, _ => new()).TryAdd(ws, 0);

    public void Remove(Guid userId, WebSocket ws)
    {
        if (!_connections.TryGetValue(userId, out var set)) return;
        set.TryRemove(ws, out _);
        // Clean up the outer entry only when the set is still the same empty instance.
        if (set.IsEmpty)
            _connections.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<WebSocket, byte>>(userId, set));
    }

    public async Task BroadcastAsync(Guid userId, string jsonPayload)
    {
        if (!_connections.TryGetValue(userId, out var set)) return;

        var bytes = Encoding.UTF8.GetBytes(jsonPayload);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var (ws, _) in set)
        {
            if (ws.State != WebSocketState.Open) { set.TryRemove(ws, out _); continue; }
            try { await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None); }
            catch { set.TryRemove(ws, out _); }
        }
    }
}
