using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record ReorderQueueCommand(
    Guid UserId,
    Guid SenderDeviceId,
    int From,
    int To) : IRequest;

public sealed class ReorderQueueCommandHandler(IPlaybackSessionStore store) : IRequestHandler<ReorderQueueCommand>
{
    public Task Handle(ReorderQueueCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        if (session.Queue.Count == 0) return Task.CompletedTask;
        if (request.From < 0 || request.From >= session.Queue.Count) return Task.CompletedTask;
        if (request.To < 0 || request.To >= session.Queue.Count) return Task.CompletedTask;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.ReorderQueue(request.From, request.To);
        return Task.CompletedTask;
    }
}
