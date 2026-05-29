using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Dtos;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ColdHarbour.Api.Playback;

/// <summary>
/// Per-user serialized command pump. Owns the in-memory PlaybackSession for one user
/// and processes all mutations through a single-reader channel, eliminating the data
/// races present in the previous hub design. Phase 1 introduced the actor; Phase 3
/// moves session hydration into the actor (LoadAsync on first command) and drops the
/// shared mutable reference that GetOrCreate used to hand out.
/// </summary>
public sealed class PlaybackUserActor : IAsyncDisposable
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly TimeSpan HeartbeatThrottle = TimeSpan.FromSeconds(5);

    // Bounded LRU set for command-id deduplication. The actor is single-threaded
    // on the pump side so no synchronisation is needed.
    private const int CommandIdCacheCapacity = 256;
    private readonly Queue<string> _seenCommandIdOrder = new(CommandIdCacheCapacity);
    private readonly HashSet<string> _seenCommandIds = new(CommandIdCacheCapacity);

    private readonly Guid _userId;
    private readonly IPlaybackSessionStore _store;
    private readonly PlaybackConnectionStore _connectionStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaybackUserActor> _logger;
    private readonly Channel<CommandEnvelope> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;

    private volatile PlaybackSession? _session;
    private volatile bool _isDisposed;
    private int _connectionCount;
    private DateTimeOffset _lastCommandTime = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastHeartbeatWrite = DateTimeOffset.MinValue;

    public PlaybackUserActor(
        Guid userId,
        IPlaybackSessionStore store,
        PlaybackConnectionStore connectionStore,
        IServiceScopeFactory scopeFactory,
        ILogger<PlaybackUserActor> logger)
    {
        _userId = userId;
        _store = store;
        _connectionStore = connectionStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<CommandEnvelope>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        });
        _pumpTask = Task.Run(() => PumpAsync(_cts.Token));
    }

    public bool IsDisposed => _isDisposed;

    public bool IsIdle =>
        _connectionCount == 0 &&
        DateTimeOffset.UtcNow - _lastCommandTime > TimeSpan.FromMinutes(5);

    public void NotifyConnected() => Interlocked.Increment(ref _connectionCount);
    public void NotifyDisconnected() => Interlocked.Decrement(ref _connectionCount);

    /// <summary>Fire-and-forget overload used by tests and internal callers.</summary>
    public ValueTask EnqueueAsync(InboundCommand cmd, CancellationToken ct)
        => EnqueueAsync(cmd, commandId: null, source: null, ct);

    /// <summary>
    /// Hub-facing overload. Wraps the command in a <see cref="CommandEnvelope"/>
    /// so the actor can unicast a <c>command-ack</c> back to the originating socket.
    /// </summary>
    public ValueTask EnqueueAsync(
        InboundCommand cmd, string? commandId, System.Net.WebSockets.WebSocket? source, CancellationToken ct)
    {
        _lastCommandTime = DateTimeOffset.UtcNow;
        return _channel.Writer.WriteAsync(new CommandEnvelope(cmd, commandId, source), ct);
    }

    /// <summary>
    /// Broadcasts the current session state to all connected sockets for this user.
    /// Safe to call from outside the pump (concurrent read of a volatile reference).
    /// </summary>
    public async Task BroadcastCurrentSessionAsync(CancellationToken ct)
    {
        var session = _session ?? await _store.LoadAsync(_userId, ct) ?? PlaybackSession.Create(_userId);
        await BroadcastSessionAsync(session, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _channel.Writer.TryComplete();
        try { await _pumpTask.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { _cts.Cancel(); }

        // Persist final state on graceful eviction / host shutdown.
        if (_session is { } session)
        {
            try { await _store.SaveAsync(session, SaveReason.Shutdown, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to save session on dispose for user {UserId}", _userId); }
        }

        _cts.Dispose();
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in _channel.Reader.ReadAllAsync(ct))
            {
                _lastCommandTime = DateTimeOffset.UtcNow;
                try { await ProcessEnvelopeAsync(envelope, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Playback command error for user {UserId}", _userId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessEnvelopeAsync(CommandEnvelope envelope, CancellationToken ct)
    {
        // Deduplicate: if we've already processed this commandId, emit noop ack and return.
        if (envelope.CommandId is { } cid && _seenCommandIds.Contains(cid))
        {
            await SendAckAsync(envelope.Source, cid, "noop", revision: null, ct);
            return;
        }

        await ProcessCommandAsync(envelope, ct);

        // Track seen commandId (bounded LRU eviction).
        if (envelope.CommandId is { } newCid)
        {
            if (_seenCommandIds.Count >= CommandIdCacheCapacity)
            {
                var oldest = _seenCommandIdOrder.Dequeue();
                _seenCommandIds.Remove(oldest);
            }
            _seenCommandIds.Add(newCid);
            _seenCommandIdOrder.Enqueue(newCid);
        }
    }

    private async Task ProcessCommandAsync(CommandEnvelope envelope, CancellationToken ct)
    {
        var cmd = envelope.Command;

        // Hydrate from store on first command; actor owns the reference from here on.
        _session ??= await _store.LoadAsync(_userId, ct) ?? PlaybackSession.Create(_userId);
        var session = _session;

        bool changed = false;
        bool isHeartbeat = false;
        bool broadcastDevices = false;

        using var scope = _scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        switch (cmd)
        {
            case SetQueueCmd c:
                changed = await mediator.Send(new SetQueueCommand(session, c.TrackIds, c.StartIndex, c.DeviceId), ct);
                break;
            case NextCmd c:
                changed = await mediator.Send(new NextTrackCommand(session, c.DeviceId), ct);
                break;
            case PreviousCmd c:
                changed = await mediator.Send(new PreviousTrackCommand(session, c.DeviceId), ct);
                break;
            case SeekCmd c:
                changed = await mediator.Send(new SeekCommand(session, c.DeviceId, c.PositionMs), ct);
                break;
            case PauseCmd c:
                if (c.DeviceId.HasValue) session.ClaimActiveIfNone(c.DeviceId.Value);
                session.Pause();
                changed = true;
                break;
            case ResumeCmd c:
                if (c.DeviceId.HasValue) session.ClaimActiveIfNone(c.DeviceId.Value);
                if (session.TrackId is not null) { session.Resume(); changed = true; }
                break;
            case HeartbeatCmd c:
                if (!IsActiveDevice(session, c.DeviceId)) return;
                changed = await mediator.Send(new UpdatePlaybackPositionCommand(session, c.PositionMs), ct);
                isHeartbeat = true;
                break;
            case TransferCmd c:
                changed = await mediator.Send(new TransferPlaybackCommand(session, c.DeviceId, c.PositionMs), ct);
                broadcastDevices = changed;
                break;
            case StopCmd c:
                if (!IsActiveDevice(session, c.DeviceId)) return;
                session.Clear();
                changed = true;
                break;
            case SetRepeatModeCmd c:
                changed = await mediator.Send(new SetRepeatModeCommand(session, c.Mode), ct);
                break;
            case SetShuffleCmd c:
                changed = await mediator.Send(new SetShuffleCommand(session, c.Enabled), ct);
                break;
            case TrackEndedCmd c:
                changed = await mediator.Send(new TrackEndedCommand(session, c.DeviceId, c.TrackId, c.DurationMs), ct);
                break;
            case AddToQueueCmd c:
                changed = await mediator.Send(new AddToQueueCommand(session, c.DeviceId, c.TrackId, c.Position), ct);
                break;
            case RemoveFromQueueCmd c:
                changed = await mediator.Send(new RemoveFromQueueCommand(session, c.DeviceId, c.Index), ct);
                break;
            case ReorderQueueCmd c:
                changed = await mediator.Send(new ReorderQueueCommand(session, c.DeviceId, c.From, c.To), ct);
                break;
            case ClearQueueCmd c:
                changed = await mediator.Send(new ClearQueueCommand(session, c.DeviceId), ct);
                break;

            case ResyncCmd:
                // Unicast the current full state to the requesting socket only.
                await UnicastSessionAsync(envelope.Source, session, ct);
                return;
        }

        if (!changed)
        {
            if (envelope.CommandId is not null)
                await SendAckAsync(envelope.Source, envelope.CommandId, "noop", revision: null, ct);
            return;
        }

        if (isHeartbeat)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastHeartbeatWrite >= HeartbeatThrottle)
            {
                await _store.SaveAsync(session, SaveReason.HeartbeatThrottled, ct);
                _lastHeartbeatWrite = now;
            }
            // Heartbeat → tiny tick broadcast (positionMs + isPlaying + revision only).
            await BroadcastTickAsync(session, ct);
        }
        else
        {
            session.IncrementRevision();
            await _store.SaveAsync(session, SaveReason.MaterialChange, ct);

            if (envelope.CommandId is not null)
                await SendAckAsync(envelope.Source, envelope.CommandId, "applied", session.Revision, ct);

            // Material change → full state broadcast.
            await BroadcastSessionAsync(session, ct);
        }

        if (broadcastDevices)
            await BroadcastDevicesAsync(mediator, ct);
    }

    private static async Task SendAckAsync(
        System.Net.WebSockets.WebSocket? socket,
        string commandId,
        string status,
        long? revision,
        CancellationToken ct)
    {
        if (socket is null || socket.State != System.Net.WebSockets.WebSocketState.Open)
            return;

        var payload = JsonSerializer.Serialize(
            new { type = "command-ack", commandId, status, revision },
            _jsonOpts);
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                System.Net.WebSockets.WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        catch { /* socket may have closed mid-send; ignore */ }
    }

    private static bool IsActiveDevice(PlaybackSession session, Guid deviceId)
        => !session.ActiveDeviceId.HasValue || session.ActiveDeviceId.Value == deviceId;

    private static PlaybackSessionDto ToDto(PlaybackSession session) => new(
        session.UserId,
        session.ActiveDeviceId,
        session.TrackId,
        session.PositionMs,
        session.IsPlaying,
        session.Queue,
        session.QueueIndex,
        session.RepeatMode,
        session.Shuffle,
        session.UpdatedAt,
        session.Revision);

    private async Task BroadcastSessionAsync(PlaybackSession session, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { type = "state", session = ToDto(session) }, _jsonOpts);
        await _connectionStore.BroadcastAsync(_userId, payload);
    }

    private async Task BroadcastTickAsync(PlaybackSession session, CancellationToken ct)
    {
        // trackId is included so the frontend can reject stale ticks (heartbeats
        // echoed for the previous track after a "next"/"transfer" command).
        var payload = JsonSerializer.Serialize(
            new { type = "tick", positionMs = session.PositionMs, isPlaying = session.IsPlaying, revision = session.Revision, trackId = session.TrackId },
            _jsonOpts);
        await _connectionStore.BroadcastAsync(_userId, payload);
    }

    private static async Task UnicastSessionAsync(
        System.Net.WebSockets.WebSocket? socket,
        PlaybackSession session,
        CancellationToken ct)
    {
        if (socket is null || socket.State != System.Net.WebSockets.WebSocketState.Open)
            return;

        var payload = System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { type = "state", session = ToDto(session) }, _jsonOpts));
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(payload),
                System.Net.WebSockets.WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        catch { }
    }

    private async Task BroadcastDevicesAsync(IMediator mediator, CancellationToken ct)
    {
        var devices = await mediator.Send(new ListDevicesQuery(_userId), ct);
        var payload = JsonSerializer.Serialize(new { type = "devices", devices }, _jsonOpts);
        await _connectionStore.BroadcastAsync(_userId, payload);
    }
}
