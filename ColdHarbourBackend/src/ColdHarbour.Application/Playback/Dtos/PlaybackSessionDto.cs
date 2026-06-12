using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Dtos;

public sealed record PlaybackSessionDto(
    Guid UserId,
    Guid? ActiveDeviceId,
    Guid? TrackId,
    long PositionMs,
    bool IsPlaying,
    IReadOnlyList<Guid> Queue,
    int QueueIndex,
    RepeatMode RepeatMode,
    bool Shuffle,
    DateTimeOffset UpdatedAt,
    long Revision = 0,
    // Server-derived live position (PositionMs interpolated by wall-clock while playing).
    // Sits alongside the canonical PositionMs + UpdatedAt so WS clients can still interpolate
    // themselves; REST/non-WS callers read this directly.
    long CurrentPositionMs = 0);
