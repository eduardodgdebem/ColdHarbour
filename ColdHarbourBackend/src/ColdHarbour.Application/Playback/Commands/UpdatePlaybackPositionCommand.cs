using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record UpdatePlaybackPositionCommand(PlaybackSession Session, long PositionMs) : IRequest<bool>;

public sealed class UpdatePlaybackPositionCommandHandler : IRequestHandler<UpdatePlaybackPositionCommand, bool>
{
    public Task<bool> Handle(UpdatePlaybackPositionCommand request, CancellationToken cancellationToken)
    {
        request.Session.UpdatePosition(request.PositionMs);
        return Task.FromResult(true);
    }
}
