using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record StartPlaybackCommand(Guid UserId, Guid DeviceId, Guid TrackId) : IRequest;

public sealed class StartPlaybackCommandHandler(
    IPlaybackSessionStore store,
    IPlayEventRepository events) : IRequestHandler<StartPlaybackCommand>
{
    public async Task Handle(StartPlaybackCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        session.Start(request.DeviceId, request.TrackId);

        var playEvent = PlayEvent.Begin(request.UserId, request.DeviceId, request.TrackId);
        await events.AddAsync(playEvent, cancellationToken);
        await events.SaveChangesAsync(cancellationToken);
    }
}
