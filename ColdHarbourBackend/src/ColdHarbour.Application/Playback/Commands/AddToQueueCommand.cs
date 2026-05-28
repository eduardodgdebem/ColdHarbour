using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record AddToQueueCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    Guid TrackId,
    int? Position) : IRequest<bool>;

public sealed class AddToQueueCommandHandler(IPlayEventRepository events) : IRequestHandler<AddToQueueCommand, bool>
{
    public async Task<bool> Handle(AddToQueueCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        var wasEmpty = session.Queue.Count == 0;

        session.AddToQueue(request.TrackId, request.Position);
        session.ClaimActiveIfNone(request.SenderDeviceId);

        if (wasEmpty &&
            session.TrackId is { } trackId &&
            session.ActiveDeviceId is { } activeDeviceId)
        {
            var begin = PlayEvent.Begin(session.UserId, activeDeviceId, trackId);
            await events.AddAsync(begin, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
