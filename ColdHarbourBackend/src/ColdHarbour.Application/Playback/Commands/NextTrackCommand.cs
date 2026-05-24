using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record NextTrackCommand(Guid UserId, Guid SenderDeviceId) : IRequest;

public sealed class NextTrackCommandHandler(
    IPlaybackSessionStore store,
    IPlayEventRepository events) : IRequestHandler<NextTrackCommand>
{
    public async Task Handle(NextTrackCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        if (session.Queue.Count == 0) return;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.AdvanceNext();

        if (session.TrackId is { } trackId && session.ActiveDeviceId is { } activeDeviceId)
        {
            var playEvent = PlayEvent.Begin(request.UserId, activeDeviceId, trackId);
            await events.AddAsync(playEvent, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }
    }
}
