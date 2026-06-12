using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record UpdatePlaybackPositionCommand(
    PlaybackSession Session, long PositionMs, int MaxForwardDriftMs = 5000) : IRequest<bool>;

public sealed class UpdatePlaybackPositionCommandHandler : IRequestHandler<UpdatePlaybackPositionCommand, bool>
{
    public Task<bool> Handle(UpdatePlaybackPositionCommand request, CancellationToken cancellationToken)
    {
        // Phase 4: the heartbeat is sanity-bounded. A rejected (out-of-range) value returns false
        // so the actor neither persists nor broadcasts it.
        var accepted = request.Session.RecordHeartbeat(request.PositionMs, request.MaxForwardDriftMs);
        return Task.FromResult(accepted);
    }
}
