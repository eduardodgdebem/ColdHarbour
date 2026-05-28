using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

/// <summary>
/// Sent by the active device when its &lt;audio&gt; element fires 'ended'.
/// Closes the open <see cref="PlayEvent"/> and advances the session honoring RepeatMode and Shuffle.
/// </summary>
public sealed record TrackEndedCommand(
    PlaybackSession Session,
    Guid SenderDeviceId,
    Guid TrackId,
    long DurationMs) : IRequest<bool>;

public sealed class TrackEndedCommandHandler(IPlayEventRepository events) : IRequestHandler<TrackEndedCommand, bool>
{
    public async Task<bool> Handle(TrackEndedCommand request, CancellationToken cancellationToken)
    {
        var session = request.Session;
        // Only trust trackEnded from the active device — stale 'ended' from a formerly-active device must not advance.
        if (session.ActiveDeviceId != request.SenderDeviceId) return false;
        if (session.TrackId != request.TrackId) return false;

        var endedTrackId = session.TrackId.Value;

        var active = await events.FindActiveByUserAsync(session.UserId, cancellationToken);
        if (active is not null && active.TrackId == endedTrackId)
            active.Complete(request.DurationMs, request.DurationMs);

        session.AdvanceAfterEnd();

        if (session.TrackId is { } nextTrackId && session.ActiveDeviceId is { } activeDeviceId)
        {
            var begin = PlayEvent.Begin(session.UserId, activeDeviceId, nextTrackId);
            await events.AddAsync(begin, cancellationToken);
        }

        await events.SaveChangesAsync(cancellationToken);
        return true;
    }
}
