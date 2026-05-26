using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record AddToQueueCommand(
    Guid UserId,
    Guid SenderDeviceId,
    Guid TrackId,
    int? Position) : IRequest;

public sealed class AddToQueueCommandHandler(
    IPlaybackSessionStore store,
    IPlayEventRepository events) : IRequestHandler<AddToQueueCommand>
{
    public async Task Handle(AddToQueueCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        var wasEmpty = session.Queue.Count == 0;

        session.AddToQueue(request.TrackId, request.Position);
        session.ClaimActiveIfNone(request.SenderDeviceId);

        // Adding to an empty queue also starts playback — open a PlayEvent
        // for the new (and only) track.
        if (wasEmpty &&
            session.TrackId is { } trackId &&
            session.ActiveDeviceId is { } activeDeviceId)
        {
            var begin = PlayEvent.Begin(request.UserId, activeDeviceId, trackId);
            await events.AddAsync(begin, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }
    }
}
