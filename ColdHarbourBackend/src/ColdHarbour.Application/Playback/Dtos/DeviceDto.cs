namespace ColdHarbour.Application.Playback.Dtos;

public sealed record DeviceDto(
    Guid Id,
    string Name,
    DateTimeOffset LastSeenAt);
