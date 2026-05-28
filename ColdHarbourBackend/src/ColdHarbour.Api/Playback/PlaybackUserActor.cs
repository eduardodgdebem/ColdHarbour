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

    private readonly Guid _userId;
    private readonly IPlaybackSessionStore _store;
    private readonly PlaybackConnectionStore _connectionStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaybackUserActor> _logger;
    private readonly Channel<InboundCommand> _channel;
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
        _channel = Channel.CreateBounded<InboundCommand>(new BoundedChannelOptions(1000)
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

    public ValueTask EnqueueAsync(InboundCommand cmd, CancellationToken ct)
    {
        _lastCommandTime = DateTimeOffset.UtcNow;
        return _channel.Writer.WriteAsync(cmd, ct);
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
            await foreach (var cmd in _channel.Reader.ReadAllAsync(ct))
            {
                _lastCommandTime = DateTimeOffset.UtcNow;
                try { await ProcessCommandAsync(cmd, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Playback command error for user {UserId}", _userId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessCommandAsync(InboundCommand cmd, CancellationToken ct)
    {
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
        }

        if (!changed) return;

        if (isHeartbeat)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastHeartbeatWrite >= HeartbeatThrottle)
            {
                await _store.SaveAsync(session, SaveReason.HeartbeatThrottled, ct);
                _lastHeartbeatWrite = now;
            }
        }
        else
        {
            await _store.SaveAsync(session, SaveReason.MaterialChange, ct);
        }

        await BroadcastSessionAsync(session, ct);

        if (broadcastDevices)
            await BroadcastDevicesAsync(mediator, ct);
    }

    private static bool IsActiveDevice(PlaybackSession session, Guid deviceId)
        => !session.ActiveDeviceId.HasValue || session.ActiveDeviceId.Value == deviceId;

    private async Task BroadcastSessionAsync(PlaybackSession session, CancellationToken ct)
    {
        var dto = new PlaybackSessionDto(
            session.UserId,
            session.ActiveDeviceId,
            session.TrackId,
            session.PositionMs,
            session.IsPlaying,
            session.Queue,
            session.QueueIndex,
            session.RepeatMode,
            session.Shuffle,
            session.UpdatedAt);
        var payload = JsonSerializer.Serialize(new { type = "session", session = dto }, _jsonOpts);
        await _connectionStore.BroadcastAsync(_userId, payload);
    }

    private async Task BroadcastDevicesAsync(IMediator mediator, CancellationToken ct)
    {
        var devices = await mediator.Send(new ListDevicesQuery(_userId), ct);
        var payload = JsonSerializer.Serialize(new { type = "devices", devices }, _jsonOpts);
        await _connectionStore.BroadcastAsync(_userId, payload);
    }
}
