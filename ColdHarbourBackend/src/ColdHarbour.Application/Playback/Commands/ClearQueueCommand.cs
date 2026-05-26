using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record ClearQueueCommand(Guid UserId, Guid SenderDeviceId) : IRequest;

public sealed class ClearQueueCommandHandler(IPlaybackSessionStore store) : IRequestHandler<ClearQueueCommand>
{
    public Task Handle(ClearQueueCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.ClearQueue();
        return Task.CompletedTask;
    }
}
