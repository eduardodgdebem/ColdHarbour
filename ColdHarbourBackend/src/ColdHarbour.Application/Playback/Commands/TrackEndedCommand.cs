using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

/// <summary>
/// Sent by the active device when its &lt;audio&gt; element fires 'ended'.
/// The handler resolves the track duration from the server (ITrackRepository)
/// and never trusts any client-supplied duration value.
/// </summary>
public sealed record TrackEndedCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    Guid TrackId) : IRequest<bool>;

public sealed class TrackEndedCommandHandler(
    IPlaySessionTimeline timeline,
    ITrackRepository tracks) : IRequestHandler<TrackEndedCommand, bool>
{
    public async Task<bool> Handle(TrackEndedCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        if (session.ActiveDeviceId != request.SenderDeviceId) return false;
        if (session.TrackId != request.TrackId) return false;

        var endedTrackId = session.TrackId.Value;
        var activeDeviceId = session.ActiveDeviceId!.Value;

        // Server-trusted duration; fall back to last heartbeat position when track is unknown.
        var track = await tracks.FindByIdAsync(endedTrackId, cancellationToken);
        var endPositionMs = track is not null
            ? (int)track.Duration.TotalMilliseconds
            : (int)session.PositionMs;

        session.AdvanceAfterEnd();

        await timeline.TrackChangedAsync(
            session.UserId, activeDeviceId,
            endedTrackId, endPositionMs,
            session.TrackId, cancellationToken);

        return true;
    }
}
