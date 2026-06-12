using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Api.Playback;

public abstract record InboundCommand;

public sealed record SetQueueCmd(Guid DeviceId, IReadOnlyList<Guid> TrackIds, int StartIndex) : InboundCommand;
public sealed record NextCmd(Guid DeviceId) : InboundCommand;
public sealed record PreviousCmd(Guid DeviceId) : InboundCommand;
public sealed record SeekCmd(Guid DeviceId, long PositionMs) : InboundCommand;
public sealed record PauseCmd(Guid? DeviceId) : InboundCommand;
public sealed record ResumeCmd(Guid? DeviceId) : InboundCommand;
public sealed record HeartbeatCmd(Guid DeviceId, long PositionMs) : InboundCommand;
public sealed record TransferCmd(Guid DeviceId, long PositionMs) : InboundCommand;
public sealed record StopCmd(Guid DeviceId) : InboundCommand;
public sealed record SetRepeatModeCmd(RepeatMode Mode) : InboundCommand;
public sealed record SetShuffleCmd(bool Enabled) : InboundCommand;
public sealed record TrackEndedCmd(Guid DeviceId, Guid TrackId, long DurationMs) : InboundCommand;
public sealed record AddToQueueCmd(Guid DeviceId, Guid TrackId, int? Position) : InboundCommand;
public sealed record RemoveFromQueueCmd(Guid DeviceId, int Index) : InboundCommand;
public sealed record ReorderQueueCmd(Guid DeviceId, int From, int To) : InboundCommand;
public sealed record ClearQueueCmd(Guid DeviceId) : InboundCommand;
public sealed record ResyncCmd(Guid DeviceId, long LastSeenRevision) : InboundCommand;
/// <summary>
/// Internal command (never parsed from a client frame). Enqueued by the hub on connect so the
/// actor can release the active device through the pump if it has gone stale. Carries no data —
/// the actor evaluates liveness against the connected-device store + the device's LastSeenAt.
/// </summary>
public sealed record CheckLivenessCmd : InboundCommand;
