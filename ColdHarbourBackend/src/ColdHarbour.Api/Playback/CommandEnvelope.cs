using System.Net.WebSockets;

namespace ColdHarbour.Api.Playback;

/// <summary>
/// Wraps an <see cref="InboundCommand"/> with the wire-protocol fields
/// <see cref="CommandId"/> and <see cref="Source"/> so the actor can
/// unicast a <c>command-ack</c> back to the originating socket without
/// polluting the domain command types.
/// </summary>
public sealed record CommandEnvelope(
    InboundCommand Command,
    string? CommandId,
    WebSocket? Source);
