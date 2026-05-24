using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SeekCommand(Guid UserId, Guid SenderDeviceId, long PositionMs) : IRequest;

public sealed class SeekCommandHandler(IPlaybackSessionStore store) : IRequestHandler<SeekCommand>
{
    public Task Handle(SeekCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        if (session.TrackId is null) return Task.CompletedTask;

        session.ClaimActiveIfNone(request.SenderDeviceId);
        session.Seek(request.PositionMs);
        return Task.CompletedTask;
    }
}
