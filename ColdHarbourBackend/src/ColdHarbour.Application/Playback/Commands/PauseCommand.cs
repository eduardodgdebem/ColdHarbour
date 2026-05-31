using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record PauseCommand(PlaybackSession Session, Guid? SenderDeviceId) : IRequest<bool>;

public sealed class PauseCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<PauseCommand, bool>
{
    public async Task<bool> Handle(PauseCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (request.SenderDeviceId.HasValue)
            session.ClaimActiveIfNone(request.SenderDeviceId.Value);
        session.Pause();
        await timeline.PausedAsync(session.UserId, DateTimeOffset.UtcNow, cancellationToken);
        return true;
    }
}
