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
    long Revision = 0);
