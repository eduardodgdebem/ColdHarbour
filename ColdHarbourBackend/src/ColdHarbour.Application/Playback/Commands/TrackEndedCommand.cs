using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

/// <summary>
/// Sent by the active device when its &lt;audio&gt; element fires 'ended'.
/// Closes the open <see cref="PlayEvent"/> (Complete) and asks the session
/// to AdvanceAfterEnd, honoring RepeatMode and Shuffle. If a new track
/// starts as a result, opens a fresh PlayEvent.Begin.
/// </summary>
public sealed record TrackEndedCommand(
    Guid UserId,
    Guid SenderDeviceId,
    Guid TrackId,
    long DurationMs) : IRequest;

public sealed class TrackEndedCommandHandler(
    IPlaybackSessionStore store,
    IPlayEventRepository events) : IRequestHandler<TrackEndedCommand>
{
    public async Task Handle(TrackEndedCommand request, CancellationToken cancellationToken)
    {
        var session = store.GetOrCreate(request.UserId);
        // Only trust trackEnded from the active device — stale 'ended' from a
        // formerly-active device must not advance the session.
        if (session.ActiveDeviceId != request.SenderDeviceId) return;
        if (session.TrackId != request.TrackId) return;

        var positionMs = request.DurationMs; // track played to its end
        var endedTrackId = session.TrackId.Value;

        // Close the open PlayEvent for this track (PlayEvent.Complete was the
        // explicit gap noted before phase 3).
        var active = await events.FindActiveByUserAsync(request.UserId, cancellationToken);
        if (active is not null && active.TrackId == endedTrackId)
        {
            active.Complete(request.DurationMs, positionMs);
        }

        session.AdvanceAfterEnd();

        // If AdvanceAfterEnd picked a new track, open its PlayEvent.
        if (session.TrackId is { } nextTrackId &&
            session.ActiveDeviceId is { } activeDeviceId)
        {
            var begin = PlayEvent.Begin(request.UserId, activeDeviceId, nextTrackId);
            await events.AddAsync(begin, cancellationToken);
        }

        await events.SaveChangesAsync(cancellationToken);
    }
}
