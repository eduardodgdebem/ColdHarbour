using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record ResumeCommand(PlaybackSession Session, Guid? SenderDeviceId) : IRequest<bool>;

public sealed class ResumeCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<ResumeCommand, bool>
{
    public async Task<bool> Handle(ResumeCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.TrackId is null) return false;
        session.ApplyTransport(request.SenderDeviceId, () => session.Resume());
        await timeline.ResumedAsync(session.UserId, DateTimeOffset.UtcNow, cancellationToken);
        return true;
    }
}
