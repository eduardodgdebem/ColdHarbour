using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record ReorderQueueCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    int From,
    int To) : IRequest<bool>;

public sealed class ReorderQueueCommandHandler : IRequestHandler<ReorderQueueCommand, bool>
{
    public Task<bool> Handle(ReorderQueueCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.Queue.Count == 0) return Task.FromResult(false);
        if (request.From < 0 || request.From >= session.Queue.Count) return Task.FromResult(false);
        if (request.To < 0 || request.To >= session.Queue.Count) return Task.FromResult(false);

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.ReorderQueue(request.From, request.To);
        return Task.FromResult(true);
    }
}
