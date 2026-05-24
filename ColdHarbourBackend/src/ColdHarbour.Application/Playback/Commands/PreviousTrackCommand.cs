using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record PreviousTrackCommand(Guid UserId, Guid SenderDeviceId) : IRequest;

public sealed class PreviousTrackCommandHandler(
    IPlaybackSessionStore store,
    IPlayEventRepository events) : IRequestHandler<PreviousTrackCommand>
{
    public async Task Handle(PreviousTrackCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        if (session.Queue.Count == 0) return;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.AdvancePrevious();

        if (session.TrackId is { } trackId && session.ActiveDeviceId is { } activeDeviceId)
        {
            var playEvent = PlayEvent.Begin(request.UserId, activeDeviceId, trackId);
            await events.AddAsync(playEvent, cancellationToken);
            await events.SaveChangesAsync(cancellationToken);
        }
    }
}
