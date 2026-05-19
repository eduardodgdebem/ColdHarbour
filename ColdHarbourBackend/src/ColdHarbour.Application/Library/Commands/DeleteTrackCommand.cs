using MediatR;

namespace ColdHarbour.Application.Library.Commands;

public sealed record DeleteTrackCommand(Guid TrackId) : IRequest;
