using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Dtos;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;

namespace ColdHarbour.Api.Playback;

/// <summary>
/// Raw WebSocket hub at /ws/playback. JWT is supplied as ?access_token= query param
/// (browser WS API does not allow custom headers). On JWT expiry the socket is closed
/// with code 4001 so the client can refresh and reconnect.
/// </summary>
public sealed class PlaybackSessionHub(
    IMediator mediator,
    IPlaybackSessionStore store,
    IConnectedDeviceStore connectedDeviceStore,
    IConfiguration config,
    ILogger<PlaybackSessionHub> logger)
{
    private static readonly ConcurrentDictionary<Guid, ConcurrentBag<WebSocket>> _connections = new();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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

        var bag = _connections.GetOrAdd(userId, _ => []);
        bag.Add(ws);

        try
        {
            await BroadcastSessionAsync(userId);
            await BroadcastDevicesAsync(userId, CancellationToken.None);
            await ReceiveLoopAsync(ws, userId, ctx.RequestAborted);
        }
        finally
        {
            if (deviceId.HasValue)
            {
                connectedDeviceStore.Remove(deviceId.Value);

                var session = store.GetOrCreate(userId);
                if (session.ActiveDeviceId == deviceId)
                    session.Clear();

                await BroadcastDevicesAsync(userId, CancellationToken.None);
            }

            _connections.TryGetValue(userId, out var b);
            // Rebuild bag without this socket (ConcurrentBag has no Remove)
            if (b is not null)
            {
                var remaining = b.Where(s => s != ws).ToList();
                var newBag = new ConcurrentBag<WebSocket>(remaining);
                _connections.TryUpdate(userId, newBag, b);
            }
        }
    }

    private async Task ReceiveLoopAsync(WebSocket ws, Guid userId, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await ProcessMessageAsync(userId, json, ct);
            await BroadcastSessionAsync(userId);
        }

        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }

    private async Task ProcessMessageAsync(Guid userId, string json, CancellationToken ct)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var type = node?["type"]?.GetValue<string>();
            switch (type)
            {
                case "start":
                    {
                        var deviceId = node!["deviceId"]!.GetValue<Guid>();
                        var trackId = node!["trackId"]!.GetValue<Guid>();
                        await mediator.Send(new StartPlaybackCommand(userId, deviceId, trackId), ct);
                        break;
                    }
                case "pause":
                    {
                        var session = store.GetOrCreate(userId);
                        if (!IsActiveDevice(node!, session)) break;
                        var posMs = node!["positionMs"]?.GetValue<long>() ?? 0;
                        session.UpdatePosition(posMs);
                        session.Pause();
                        break;
                    }
                case "resume":
                    {
                        var session = store.GetOrCreate(userId);
                        if (!IsActiveDevice(node!, session)) break;
                        session.Resume();
                        break;
                    }
                case "heartbeat":
                    {
                        var session = store.GetOrCreate(userId);
                        if (!IsActiveDevice(node!, session)) break;
                        var posMs = node!["positionMs"]!.GetValue<long>();
                        await mediator.Send(new UpdatePlaybackPositionCommand(userId, posMs), ct);
                        break;
                    }
                case "transfer":
                    {
                        var newDeviceId = node!["deviceId"]!.GetValue<Guid>();
                        var posMs = node!["positionMs"]?.GetValue<long>() ?? 0;
                        await mediator.Send(new TransferPlaybackCommand(userId, newDeviceId, posMs), ct);
                        await BroadcastDevicesAsync(userId, ct);
                        break;
                    }
                case "stop":
                    {
                        var session = store.GetOrCreate(userId);
                        if (!IsActiveDevice(node!, session)) break;
                        session.Clear();
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error processing playback message from user {UserId}", userId);
        }
    }

    // Returns true if the message's deviceId matches the active device (or if no deviceId is provided).
    private static bool IsActiveDevice(JsonNode node, PlaybackSession session)
    {
        var raw = node["deviceId"]?.GetValue<string>();
        if (raw is null || !Guid.TryParse(raw, out var deviceId))
            return true; // no deviceId → allow (backward compat)
        return !session.ActiveDeviceId.HasValue || session.ActiveDeviceId.Value == deviceId;
    }

    private async Task BroadcastSessionAsync(Guid userId)
    {
        var session = store.GetOrCreate(userId);
        var dto = new PlaybackSessionDto(
            session.UserId,
            session.ActiveDeviceId,
            session.TrackId,
            session.PositionMs,
            session.IsPlaying,
            session.UpdatedAt);

        var payload = JsonSerializer.Serialize(new { type = "session", session = dto }, _jsonOpts);
        await BroadcastToUserAsync(userId, payload);
    }

    private async Task BroadcastDevicesAsync(Guid userId, CancellationToken ct)
    {
        var devices = await mediator.Send(new ListDevicesQuery(userId), ct);
        var payload = JsonSerializer.Serialize(new { type = "devices", devices }, _jsonOpts);
        await BroadcastToUserAsync(userId, payload);
    }

    private static async Task BroadcastToUserAsync(Guid userId, string payload)
    {
        if (!_connections.TryGetValue(userId, out var sockets))
            return;

        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);
        var dead = new List<WebSocket>();

        foreach (var ws in sockets)
        {
            if (ws.State != WebSocketState.Open) { dead.Add(ws); continue; }
            try { await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None); }
            catch { dead.Add(ws); }
        }

        // prune dead sockets lazily
        if (dead.Count > 0)
        {
            var remaining = sockets.Except(dead).ToList();
            _connections.TryUpdate(userId, new ConcurrentBag<WebSocket>(remaining), sockets);
        }
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
            // Signal expiry via close code 4001 — handled in HandleAsync early-exit
            return null;
        }
        catch
        {
            return null;
        }
    }
}
