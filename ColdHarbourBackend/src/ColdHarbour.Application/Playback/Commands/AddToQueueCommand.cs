using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record AddToQueueCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    Guid TrackId,
    int? Position) : IRequest<bool>;

public sealed class AddToQueueCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<AddToQueueCommand, bool>
{
    public async Task<bool> Handle(AddToQueueCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        var wasEmpty = session.Queue.Count == 0;
        var oldTrackId = session.TrackId;
        var oldPositionMs = (int)session.PositionMs;

        session.ApplyTransport(request.SenderDeviceId, () => session.AddToQueue(request.TrackId, request.Position));

        if (wasEmpty && session.TrackId.HasValue && session.ActiveDeviceId is { } activeDeviceId)
            await timeline.TrackChangedAsync(
                session.UserId, activeDeviceId,
                oldTrackId, oldPositionMs,
                session.TrackId, cancellationToken);

        return true;
    }
}
