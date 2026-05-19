namespace ColdHarbour.Application.Playback.Dtos;

public sealed record PlaybackSessionDto(
    Guid UserId,
    Guid? ActiveDeviceId,
    Guid? TrackId,
    long PositionMs,
    bool IsPlaying,
    DateTimeOffset UpdatedAt);
