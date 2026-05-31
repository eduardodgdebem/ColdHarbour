using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record TransferPlaybackCommand(PlaybackSession Session, Guid NewDeviceId, long PositionMs) : IRequest<bool>;

public sealed class TransferPlaybackCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<TransferPlaybackCommand, bool>
{
    public async Task<bool> Handle(TransferPlaybackCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        var oldDeviceId = session.ActiveDeviceId;

        session.Transfer(request.NewDeviceId, request.PositionMs);

        await timeline.ActiveDeviceChangedAsync(
            session.UserId,
            oldDeviceId,
            (int)request.PositionMs,
            session.ActiveDeviceId,
            cancellationToken);

        return true;
    }
}
