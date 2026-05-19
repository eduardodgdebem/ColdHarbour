namespace ColdHarbour.Application.Library.Dtos;

public sealed record TrackUploadResultDto(Guid TrackId, Guid AlbumId, bool AlreadyExisted);
