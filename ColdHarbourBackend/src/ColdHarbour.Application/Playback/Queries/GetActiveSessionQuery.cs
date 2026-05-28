using ColdHarbour.Application.Playback.Dtos;
using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

public sealed record GetActiveSessionQuery(Guid UserId) : IRequest<PlaybackSessionDto?>;

public sealed class GetActiveSessionQueryHandler(IPlaybackSessionStore store) : IRequestHandler<GetActiveSessionQuery, PlaybackSessionDto?>
{
    public async Task<PlaybackSessionDto?> Handle(GetActiveSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await store.LoadAsync(request.UserId, cancellationToken);
        if (session?.TrackId is null)
            return null;

        return new PlaybackSessionDto(
            session.UserId,
            session.ActiveDeviceId,
            session.TrackId,
            session.PositionMs,
            session.IsPlaying,
            session.Queue,
            session.QueueIndex,
            session.RepeatMode,
            session.Shuffle,
            session.UpdatedAt);
    }
}
