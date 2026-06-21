using MediatR;

namespace ColdHarbour.Application.Library.Commands;

public sealed record UpdateAlbumCommand(Guid AlbumId, string Title, int? Year) : IRequest;
