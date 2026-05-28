using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record NextTrackCommand(PlaybackSession Session, Guid SenderDeviceId) : IRequest<bool>;

public sealed class NextTrackCommandHandler(IPlayEventRepository events) : IRequestHandler<NextTrackCommand, bool>
{
    public async Task<bool> Handle(NextTrackCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.Queue.Count == 0) return false;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.AdvanceNext();

        if (session.TrackId is { } trackId && session.ActiveDeviceId is { } activeDeviceId)
        {
            var playEvent = PlayEvent.Begin(session.UserId, activeDeviceId, trackId);
            await events.AddAsync(playEvent, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
