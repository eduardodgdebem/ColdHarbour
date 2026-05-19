using ColdHarbour.Application.Playback.Dtos;
using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record GetActiveSessionQuery(Guid UserId) : IRequest<PlaybackSessionDto?>;

public sealed class GetActiveSessionQueryHandler(IPlaybackSessionStore store) : IRequestHandler<GetActiveSessionQuery, PlaybackSessionDto?>
{
    public Task<PlaybackSessionDto?> Handle(GetActiveSessionQuery request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        if (session.TrackId is null)
            return Task.FromResult<PlaybackSessionDto?>(null);

        return Task.FromResult<PlaybackSessionDto?>(new PlaybackSessionDto(
            session.UserId,
            session.ActiveDeviceId,
            session.TrackId,
            session.PositionMs,
            session.IsPlaying,
            session.UpdatedAt));
    }
}
