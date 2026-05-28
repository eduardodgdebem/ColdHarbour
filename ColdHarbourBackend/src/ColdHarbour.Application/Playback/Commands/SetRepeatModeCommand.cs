using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SetRepeatModeCommand(PlaybackSession Session, RepeatMode Mode) : IRequest<bool>;

public sealed class SetRepeatModeCommandHandler : IRequestHandler<SetRepeatModeCommand, bool>
{
    public Task<bool> Handle(SetRepeatModeCommand request, CancellationToken cancellationToken)
    {
        request.Session.SetRepeatMode(request.Mode);
        return Task.FromResult(true);
    }
}
