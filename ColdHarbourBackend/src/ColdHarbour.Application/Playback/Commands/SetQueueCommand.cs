using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SetQueueCommand(Guid UserId, IReadOnlyList<Guid> TrackIds, int StartIndex) : IRequest;

public sealed class SetQueueCommandHandler(IPlaybackSessionStore store) : IRequestHandler<SetQueueCommand>
{
    public Task Handle(SetQueueCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        session.SetQueue(request.TrackIds, request.StartIndex);
        return Task.CompletedTask;
    }
}
