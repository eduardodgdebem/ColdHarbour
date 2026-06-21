using MediatR;

namespace ColdHarbour.Application.Library.Commands;

public sealed record UpdateTrackCommand(Guid TrackId, string Title, int? TrackNumber) : IRequest;
