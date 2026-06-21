using MediatR;

namespace ColdHarbour.Application.Library.Commands;

/// <summary>
/// Replace an album's cover from an uploaded image. The handler validates the
/// image (MIME + magic bytes + size) via IArtworkService, writes it to
/// cache/art/ (never library/), and points the album at the new sha1.
/// </summary>
public sealed record UpdateAlbumCoverCommand(Guid AlbumId, Stream Content, string ContentType) : IRequest;
