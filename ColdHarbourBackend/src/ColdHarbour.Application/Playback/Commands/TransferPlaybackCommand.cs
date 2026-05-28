using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record TransferPlaybackCommand(PlaybackSession Session, Guid NewDeviceId, long PositionMs) : IRequest<bool>;

public sealed class TransferPlaybackCommandHandler : IRequestHandler<TransferPlaybackCommand, bool>
{
    public Task<bool> Handle(TransferPlaybackCommand request, CancellationToken cancellationToken)
    {
        request.Session.Transfer(request.NewDeviceId, request.PositionMs);
        return Task.FromResult(true);
    }
}
