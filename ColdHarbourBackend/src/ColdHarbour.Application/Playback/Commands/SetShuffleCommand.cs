using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SetShuffleCommand(Guid UserId, bool Enabled) : IRequest;

public sealed class SetShuffleCommandHandler(IPlaybackSessionStore store) : IRequestHandler<SetShuffleCommand>
{
    public Task Handle(SetShuffleCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        session.SetShuffle(request.Enabled);
        return Task.CompletedTask;
    }
}
