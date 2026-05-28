using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record PreviousTrackCommand(PlaybackSession Session, Guid SenderDeviceId) : IRequest<bool>;

public sealed class PreviousTrackCommandHandler(IPlayEventRepository events) : IRequestHandler<PreviousTrackCommand, bool>
{
    public async Task<bool> Handle(PreviousTrackCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.Queue.Count == 0) return false;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.AdvancePrevious();

        if (session.TrackId is { } trackId && session.ActiveDeviceId is { } activeDeviceId)
        {
            var playEvent = PlayEvent.Begin(session.UserId, activeDeviceId, trackId);
            await events.AddAsync(playEvent, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
