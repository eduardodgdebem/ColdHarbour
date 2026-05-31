using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record PreviousTrackCommand(PlaybackSession Session, Guid SenderDeviceId) : IRequest<bool>;

public sealed class PreviousTrackCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<PreviousTrackCommand, bool>
{
    public async Task<bool> Handle(PreviousTrackCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.Queue.Count == 0) return false;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        var oldTrackId = session.TrackId;
        var oldPositionMs = (int)session.PositionMs;
        session.AdvancePrevious();

        if (session.ActiveDeviceId is { } activeDeviceId)
            await timeline.TrackChangedAsync(
                session.UserId, activeDeviceId,
                oldTrackId, oldPositionMs,
                session.TrackId, cancellationToken);

        return true;
    }
}
