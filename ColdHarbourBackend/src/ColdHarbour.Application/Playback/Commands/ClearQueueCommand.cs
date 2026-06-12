using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record ClearQueueCommand(PlaybackSession Session, Guid SenderDeviceId) : IRequest<bool>;

public sealed class ClearQueueCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<ClearQueueCommand, bool>
{
    public async Task<bool> Handle(ClearQueueCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        var oldPositionMs = (int)session.PositionMs;
        session.ApplyTransport(request.SenderDeviceId, () => session.ClearQueue());
        await timeline.SessionClearedAsync(session.UserId, oldPositionMs, cancellationToken);
        return true;
    }
}
