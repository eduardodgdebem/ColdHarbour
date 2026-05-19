using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record TransferPlaybackCommand(Guid UserId, Guid NewDeviceId, long PositionMs) : IRequest;

public sealed class TransferPlaybackCommandHandler(IPlaybackSessionStore store) : IRequestHandler<TransferPlaybackCommand>
{
    public Task Handle(TransferPlaybackCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        session.Transfer(request.NewDeviceId, request.PositionMs);
        return Task.CompletedTask;
    }
}
