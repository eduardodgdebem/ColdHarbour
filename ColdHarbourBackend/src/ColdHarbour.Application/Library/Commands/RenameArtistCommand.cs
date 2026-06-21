using MediatR;

namespace ColdHarbour.Application.Library.Commands;

public sealed record RenameArtistCommand(Guid ArtistId, string Name) : IRequest;
