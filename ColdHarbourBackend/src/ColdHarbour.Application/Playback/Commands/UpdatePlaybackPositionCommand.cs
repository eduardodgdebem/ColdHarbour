using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record UpdatePlaybackPositionCommand(Guid UserId, long PositionMs) : IRequest;

public sealed class UpdatePlaybackPositionCommandHandler(IPlaybackSessionStore store) : IRequestHandler<UpdatePlaybackPositionCommand>
{
    public Task Handle(UpdatePlaybackPositionCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        session.UpdatePosition(request.PositionMs);
        return Task.CompletedTask;
    }
}
