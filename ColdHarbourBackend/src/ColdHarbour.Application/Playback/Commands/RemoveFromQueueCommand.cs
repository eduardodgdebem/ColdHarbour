using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record RemoveFromQueueCommand(
    Guid UserId,
    Guid SenderDeviceId,
    int Index) : IRequest;

public sealed class RemoveFromQueueCommandHandler(
    IPlaybackSessionStore store,
    IPlayEventRepository events) : IRequestHandler<RemoveFromQueueCommand>
{
    public async Task Handle(RemoveFromQueueCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        if (request.Index < 0 || request.Index >= session.Queue.Count) return;

        var previouslyPlayingTrack = session.TrackId;
        var wasRemovingCurrentTrack =
            session.QueueIndex == request.Index && previouslyPlayingTrack is not null;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.RemoveFromQueue(request.Index);

        // If the currently-playing track was removed and a different one is
        // now playing in its place, close the old PlayEvent + open a new one.
        if (wasRemovingCurrentTrack &&
            session.TrackId is { } newTrackId &&
            newTrackId != previouslyPlayingTrack &&
            session.ActiveDeviceId is { } activeDeviceId)
        {
            var active = await events.FindActiveByUserAsync(request.UserId, cancellationToken);
            if (active is not null && active.TrackId == previouslyPlayingTrack)
            {
                // We don't know how far the user got; record completion using
                // the current position as both duration and position so the
                // ratio is sensible (it's a forced skip, not a natural end).
                active.Complete(session.PositionMs, session.PositionMs);
            }

            var begin = PlayEvent.Begin(request.UserId, activeDeviceId, newTrackId);
            await events.AddAsync(begin, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }
    }
}
