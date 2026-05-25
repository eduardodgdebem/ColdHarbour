using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SetRepeatModeCommand(Guid UserId, RepeatMode Mode) : IRequest;

public sealed class SetRepeatModeCommandHandler(IPlaybackSessionStore store) : IRequestHandler<SetRepeatModeCommand>
{
    public Task Handle(SetRepeatModeCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        session.SetRepeatMode(request.Mode);
        return Task.CompletedTask;
    }
}
