using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record RemoveFromQueueCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    int Index) : IRequest<bool>;

public sealed class RemoveFromQueueCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<RemoveFromQueueCommand, bool>
{
    public async Task<bool> Handle(RemoveFromQueueCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (request.Index < 0 || request.Index >= session.Queue.Count) return false;

        var oldTrackId = session.TrackId;
        var oldPositionMs = (int)session.PositionMs;
        var wasRemovingCurrentTrack = session.QueueIndex == request.Index && oldTrackId is not null;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.RemoveFromQueue(request.Index);

        if (wasRemovingCurrentTrack
            && session.TrackId != oldTrackId
            && session.ActiveDeviceId is { } activeDeviceId)
        {
            await timeline.TrackChangedAsync(
                session.UserId, activeDeviceId,
                oldTrackId, oldPositionMs,
                session.TrackId, cancellationToken);
        }

        return true;
    }
}
