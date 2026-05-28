using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record RemoveFromQueueCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    int Index) : IRequest<bool>;

public sealed class RemoveFromQueueCommandHandler(IPlayEventRepository events) : IRequestHandler<RemoveFromQueueCommand, bool>
{
    public async Task<bool> Handle(RemoveFromQueueCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (request.Index < 0 || request.Index >= session.Queue.Count) return false;

        var previouslyPlayingTrack = session.TrackId;
        var wasRemovingCurrentTrack =
            session.QueueIndex == request.Index && previouslyPlayingTrack is not null;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.RemoveFromQueue(request.Index);

        if (wasRemovingCurrentTrack &&
            session.TrackId is { } newTrackId &&
            newTrackId != previouslyPlayingTrack &&
            session.ActiveDeviceId is { } activeDeviceId)
        {
            var active = await events.FindActiveByUserAsync(session.UserId, cancellationToken);
            if (active is not null && active.TrackId == previouslyPlayingTrack)
                active.Complete(session.PositionMs, session.PositionMs);

            var begin = PlayEvent.Begin(session.UserId, activeDeviceId, newTrackId);
            await events.AddAsync(begin, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
