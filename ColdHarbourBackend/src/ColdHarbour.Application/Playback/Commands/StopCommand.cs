using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record StopCommand(PlaybackSession Session, Guid SenderDeviceId) : IRequest<bool>;

public sealed class StopCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<StopCommand, bool>
{
    public async Task<bool> Handle(StopCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        var oldPositionMs = (int)session.PositionMs;

        // Stop never claims active — it must not install an owner on an empty session.
        session.Clear();
        await timeline.SessionClearedAsync(session.UserId, oldPositionMs, cancellationToken);
        return true;
    }
}
