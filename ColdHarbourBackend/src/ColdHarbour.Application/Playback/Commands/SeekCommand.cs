using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record SeekCommand(PlaybackSession Session, Guid SenderDeviceId, long PositionMs) : IRequest<bool>;

public sealed class SeekCommandHandler : IRequestHandler<SeekCommand, bool>
{
    public Task<bool> Handle(SeekCommand request, CancellationToken cancellationToken)
    {
        if (request.Session.TrackId is null) return Task.FromResult(false);

        request.Session.ApplyTransport(request.SenderDeviceId, () => request.Session.Seek(request.PositionMs));
        return Task.FromResult(true);
    }
}
