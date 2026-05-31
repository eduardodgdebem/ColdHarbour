using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

/// <summary>
/// Sent by the active device when its &lt;audio&gt; element fires 'ended'.
/// Closes the open <see cref="PlayEvent"/> via the timeline and advances the session
/// honoring RepeatMode and Shuffle.
/// </summary>
public sealed record TrackEndedCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    Guid TrackId,
    long DurationMs) : IRequest<bool>;

public sealed class TrackEndedCommandHandler(IPlaySessionTimeline timeline) : IRequestHandler<TrackEndedCommand, bool>
{
    public async Task<bool> Handle(TrackEndedCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.ActiveDeviceId != request.SenderDeviceId) return false;
        if (session.TrackId != request.TrackId) return false;

        var endedTrackId = session.TrackId.Value;
        var activeDeviceId = session.ActiveDeviceId!.Value;

        session.AdvanceAfterEnd();

        await timeline.TrackChangedAsync(
            session.UserId, activeDeviceId,
            endedTrackId, (int)request.DurationMs,
            session.TrackId, cancellationToken);

        return true;
    }
}
