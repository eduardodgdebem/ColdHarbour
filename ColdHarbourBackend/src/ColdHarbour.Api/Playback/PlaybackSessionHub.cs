using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace ColdHarbour.Api.Playback;

/// <summary>
/// Raw WebSocket hub at /ws/playback. JWT is supplied as ?access_token= query param
/// (browser WS API does not allow custom headers). On JWT expiry the socket is closed
/// with code 4001 so the client can refresh and reconnect.
///
/// Phase 1 change: the hub is now a thin message parser. Each inbound WS frame is
/// parsed into an InboundCommand and written to the per-user PlaybackUserActor's channel.
/// The actor serializes all mutations for a given user, eliminating the data races
/// that existed when multiple WS receive loops mutated the same PlaybackSession reference.
/// </summary>
public sealed class PlaybackSessionHub(
    IMediator mediator,
    IConnectedDeviceStore connectedDeviceStore,
    PlaybackConnectionStore connectionStore,
    PlaybackUserActorRegistry registry,
    IConfiguration config,
    ILogger<PlaybackSessionHub> logger)
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task HandleAsync(HttpContext ctx, WebSocket ws)
    {
        var auth = Authenticate(ctx);
        if (auth is null)
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
            return;
        }

        var (userId, deviceId) = auth.Value;

        if (deviceId.HasValue)
            connectedDeviceStore.Add(deviceId.Value);

        connectionStore.Add(userId, ws);
        var actor = registry.GetOrCreate(userId);
        actor.NotifyConnected();

        try
        {
            await actor.BroadcastCurrentSessionAsync(CancellationToken.None);
            await BroadcastDevicesAsync(userId, CancellationToken.None);
            await ReceiveLoopAsync(ws, userId, actor, ctx.RequestAborted);
        }
        finally
        {
            actor.NotifyDisconnected();
            connectionStore.Remove(userId, ws);

            if (deviceId.HasValue)
            {
                connectedDeviceStore.Remove(deviceId.Value);
                ApplyDisconnectPolicy(userId, deviceId.Value);
                await BroadcastDevicesAsync(userId, CancellationToken.None);
            }
        }
    }

    private async Task ReceiveLoopAsync(WebSocket ws, Guid userId, PlaybackUserActor actor, CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var (cmd, commandId) = ParseCommandWithId(json);
                if (cmd is not null)
                    await actor.EnqueueAsync(cmd, commandId, ws, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(PickCloseStatus(ct), "bye", CancellationToken.None);
        }
    }

    /// <summary>
    /// Parses a raw JSON WS frame into a typed InboundCommand plus the optional
    /// <c>commandId</c> field (client-generated UUID for ack correlation).
    /// Returns <c>(null, null)</c> for unrecognised or malformed messages.
    /// </summary>
    internal (InboundCommand? Command, string? CommandId) ParseCommandWithId(string json)
    {
        var cmd = ParseCommand(json);
        if (cmd is null) return (null, null);
        try
        {
            var node = JsonNode.Parse(json);
            var commandId = node?["commandId"]?.GetValue<string>();
            return (cmd, commandId);
        }
        catch { return (cmd, null); }
    }

    /// <summary>
    /// Parses a raw JSON WS frame into a typed InboundCommand.
    /// Returns null for unrecognised or malformed messages — these are silently dropped
    /// and never enter the actor's channel, keeping the channel free of junk.
    /// </summary>
    internal InboundCommand? ParseCommand(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var type = node?["type"]?.GetValue<string>();
            return type switch
            {
                "setQueue" => new SetQueueCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    (node["trackIds"] as JsonArray)?.Select(n => n!.GetValue<Guid>()).ToArray() ?? [],
                    node["startIndex"]?.GetValue<int>() ?? 0),

                "next" => new NextCmd(node!["deviceId"]!.GetValue<Guid>()),
                "previous" => new PreviousCmd(node!["deviceId"]!.GetValue<Guid>()),

                "seek" => new SeekCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    node["positionMs"]!.GetValue<long>()),

                "pause" => new PauseCmd(ParseOptionalGuid(node?["deviceId"])),
                "resume" => new ResumeCmd(ParseOptionalGuid(node?["deviceId"])),

                "heartbeat" => new HeartbeatCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    node["positionMs"]!.GetValue<long>()),

                "transfer" => new TransferCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    node["positionMs"]?.GetValue<long>() ?? 0),

                "stop" => new StopCmd(node!["deviceId"]!.GetValue<Guid>()),

                "setRepeatMode" when Enum.TryParse<RepeatMode>(
                    node!["mode"]?.GetValue<string>(), ignoreCase: true, out var mode)
                    => new SetRepeatModeCmd(mode),

                "setShuffle" => new SetShuffleCmd(node!["enabled"]?.GetValue<bool>() ?? false),

                "trackEnded" => new TrackEndedCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    node["trackId"]!.GetValue<Guid>(),
                    node["durationMs"]?.GetValue<long>() ?? 0),

                "addToQueue" => new AddToQueueCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    node["trackId"]!.GetValue<Guid>(),
                    node["position"]?.GetValue<int?>()),

                "removeFromQueue" => new RemoveFromQueueCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    node["index"]!.GetValue<int>()),

                "reorderQueue" => new ReorderQueueCmd(
                    node!["deviceId"]!.GetValue<Guid>(),
                    node["from"]!.GetValue<int>(),
                    node["to"]!.GetValue<int>()),

                "clearQueue" => new ClearQueueCmd(node!["deviceId"]!.GetValue<Guid>()),

                _ => null
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse playback WS message");
            return null;
        }
    }

    /// <summary>
    /// Picks the WebSocket close status based on whether the server is shutting down.
    /// </summary>
    internal static WebSocketCloseStatus PickCloseStatus(CancellationToken requestAborted) =>
        requestAborted.IsCancellationRequested
            ? WebSocketCloseStatus.EndpointUnavailable
            : WebSocketCloseStatus.NormalClosure;

    /// <summary>
    /// Session policy when a device disconnects. The playback session is durable —
    /// a disconnect is a page refresh / network blip / app close, not an intentional stop.
    /// Only an explicit <c>stop</c> message clears the session.
    /// </summary>
    internal static void ApplyDisconnectPolicy(Guid userId, Guid disconnectingDeviceId)
    {
        // Intentionally a no-op — session outlives any single connection.
    }

    private async Task BroadcastDevicesAsync(Guid userId, CancellationToken ct)
    {
        var devices = await mediator.Send(new ListDevicesQuery(userId), ct);
        var payload = JsonSerializer.Serialize(new { type = "devices", devices }, _jsonOpts);
        await connectionStore.BroadcastAsync(userId, payload);
    }

    private static Guid? ParseOptionalGuid(JsonNode? node)
    {
        var raw = node?.GetValue<string>();
        return raw is not null && Guid.TryParse(raw, out var g) ? g : null;
    }

    private (Guid userId, Guid? deviceId)? Authenticate(HttpContext ctx)
    {
        var token = ctx.Request.Query["access_token"].FirstOrDefault()
            ?? ctx.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "");

        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            var key = config["COLDHARBOUR_JWT_SIGNING_KEY"]!;
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["COLDHARBOUR_JWT_ISSUER"] ?? "coldharbour",
                ValidateAudience = true,
                ValidAudience = config["COLDHARBOUR_JWT_AUDIENCE"] ?? "coldharbour-web",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            }, out _);

            var sub = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                   ?? principal.FindFirst("sub")?.Value;

            if (!Guid.TryParse(sub, out var userId))
                return null;

            var deviceIdRaw = principal.FindFirst("deviceId")?.Value;
            Guid? deviceId = Guid.TryParse(deviceIdRaw, out var did) ? did : null;

            return (userId, deviceId);
        }
        catch (SecurityTokenExpiredException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
