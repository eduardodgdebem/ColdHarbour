using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SetShuffleCommand(PlaybackSession Session, bool Enabled) : IRequest<bool>;

public sealed class SetShuffleCommandHandler : IRequestHandler<SetShuffleCommand, bool>
{
    public Task<bool> Handle(SetShuffleCommand request, CancellationToken cancellationToken)
    {
        request.Session.SetShuffle(request.Enabled);
        return Task.FromResult(true);
    }
}
