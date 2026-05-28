using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SetQueueCommand(
    PlaybackSession Session,
    IReadOnlyList<Guid> TrackIds,
    int StartIndex,
    Guid SenderDeviceId) : IRequest<bool>;

public sealed class SetQueueCommandHandler(IPlayEventRepository events) : IRequestHandler<SetQueueCommand, bool>
{
    public async Task<bool> Handle(SetQueueCommand request, CancellationToken cancellationToken)
    {
        if (request.TrackIds.Count == 0) return false;

        var session = request.Session;
        session.SetQueue(request.TrackIds, request.StartIndex);
        session.ClaimActiveIfNone(request.SenderDeviceId);

        if (session.TrackId is { } trackId && session.ActiveDeviceId is { } activeDeviceId)
        {
            var playEvent = PlayEvent.Begin(session.UserId, activeDeviceId, trackId);
            await events.AddAsync(playEvent, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
