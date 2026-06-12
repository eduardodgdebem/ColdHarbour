using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SetQueueCommand(
    PlaybackSession Session,
    IReadOnlyList<Guid> TrackIds,
    int StartIndex,
    Guid SenderDeviceId) : IRequest<bool>;

public sealed class SetQueueCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<SetQueueCommand, bool>
{
    public async Task<bool> Handle(SetQueueCommand request, CancellationToken cancellationToken)
    {
        if (request.TrackIds.Count == 0) return false;

        var session = request.Session;
        var oldTrackId = session.TrackId;
        var oldPositionMs = (int)session.PositionMs;

        session.ApplyTransport(request.SenderDeviceId, () => session.SetQueue(request.TrackIds, request.StartIndex));

        if (session.ActiveDeviceId is { } activeDeviceId)
            await timeline.TrackChangedAsync(
                session.UserId, activeDeviceId,
                oldTrackId, oldPositionMs,
                session.TrackId, cancellationToken);

        return true;
    }
}
