using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record NextTrackCommand(PlaybackSession Session, Guid SenderDeviceId) : IRequest<bool>;

public sealed class NextTrackCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<NextTrackCommand, bool>
{
    public async Task<bool> Handle(NextTrackCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.Queue.Count == 0) return false;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        var oldTrackId = session.TrackId;
        var oldPositionMs = (int)session.PositionMs;
        session.AdvanceNext();

        if (session.ActiveDeviceId is { } activeDeviceId)
            await timeline.TrackChangedAsync(
                session.UserId, activeDeviceId,
                oldTrackId, oldPositionMs,
                session.TrackId, cancellationToken);

        return true;
    }
}
