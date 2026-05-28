using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record ClearQueueCommand(PlaybackSession Session, Guid SenderDeviceId) : IRequest<bool>;

public sealed class ClearQueueCommandHandler : IRequestHandler<ClearQueueCommand, bool>
{
    public Task<bool> Handle(ClearQueueCommand request, CancellationToken cancellationToken)
    {
        request.Session.ClaimActiveIfNone(request.SenderDeviceId);
        request.Session.ClearQueue();
        return Task.FromResult(true);
    }
}
