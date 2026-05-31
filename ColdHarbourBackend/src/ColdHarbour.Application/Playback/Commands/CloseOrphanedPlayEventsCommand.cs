using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Ports;
using MediatR;

namespace ColdHarbour.Application.Playback.Commands;

/// <summary>
/// Idempotent one-shot command that heuristically closes every open PlayEvent older than
/// <see cref="Before"/> (default: now − 1 day). Designed to run once after Phase 2 is
/// deployed to clean up events leaked by the pre-Phase-2 handler code.
///
/// Re-running is a no-op: rows with EndedAt already set are not in the result set,
/// so the command processes 0 rows and returns 0.
///
/// Exposed via: dotnet run --project ColdHarbour.Api -- close-orphans
/// </summary>
/// <param name="Before">
/// Cutoff time. Events started before this moment are candidates. Defaults to
/// <see cref="DateTimeOffset.UtcNow"/> − 1 day. Override in tests to target freshly-created rows.
/// </param>
public sealed record CloseOrphanedPlayEventsCommand(DateTimeOffset? Before = null) : IRequest<int>;

public sealed class CloseOrphanedPlayEventsCommandHandler(
    IPlayEventRepository events,
    ITrackRepository tracks) : IRequestHandler<CloseOrphanedPlayEventsCommand, int>
{
    private static readonly TimeSpan OneDayCap = TimeSpan.FromDays(1);

    public async Task<int> Handle(CloseOrphanedPlayEventsCommand request, CancellationToken cancellationToken)
    {
        var before = request.Before ?? DateTimeOffset.UtcNow.AddDays(-1);
        var orphans = await events.FindOrphanedAsync(before, cancellationToken);
        if (orphans.Count == 0) return 0;

        var now = DateTimeOffset.UtcNow;

        foreach (var ev in orphans)
        {
            var track = await tracks.FindByIdAsync(ev.TrackId, cancellationToken);
            var trackDuration = track?.Duration ?? OneDayCap;
            var cap = trackDuration < OneDayCap ? trackDuration : OneDayCap;

            var endedAt = ev.StartedAt + cap;
            var listenedMs = Math.Max(0L,
                Math.Min((long)trackDuration.TotalMilliseconds,
                         (long)(endedAt - ev.StartedAt).TotalMilliseconds));

            ev.CloseAsOrphan(endedAt, listenedMs, now);
        }

        await events.SaveChangesAsync(cancellationToken);
        return orphans.Count;
    }
}
