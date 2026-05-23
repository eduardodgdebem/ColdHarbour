namespace ColdHarbour.Application.Playback.Dtos;

public sealed record PlaybackSessionDto(
    Guid UserId,
    Guid? ActiveDeviceId,
    Guid? TrackId,
    long PositionMs,
    bool IsPlaying,
    IReadOnlyList<Guid> Queue,
    int QueueIndex,
    DateTimeOffset UpdatedAt);
