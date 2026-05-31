using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;

namespace ColdHarbour.Application.Playback.Services;

/// <summary>
/// Centralises all PlayEvent open/close decisions.
/// The only class in Application that calls IPlayEventRepository directly;
/// command handlers depend on IPlaySessionTimeline instead.
/// </summary>
public sealed class PlaySessionTimeline(IPlayEventRepository events, ITrackRepository tracks) : IPlaySessionTimeline
{
    public async Task TrackChangedAsync(
        Guid userId,
        Guid deviceId,
        Guid? oldTrackId,
        int oldPositionMs,
        Guid? newTrackId,
        CancellationToken ct)
    {
        var active = await events.FindActiveByUserAsync(userId, ct);
        if (active is not null)
            active.Complete(await ResolveTrackDurationMs(oldTrackId, oldPositionMs, ct), oldPositionMs);

        if (newTrackId.HasValue)
            await events.AddAsync(PlayEvent.Begin(userId, deviceId, newTrackId.Value), ct);

        await events.SaveChangesAsync(ct);
    }

    public async Task ActiveDeviceChangedAsync(
        Guid userId,
        Guid? oldDeviceId,
        int oldPositionMs,
        Guid? newDeviceId,
        CancellationToken ct)
    {
        var active = await events.FindActiveByUserAsync(userId, ct);
        if (active is null) return;

        var trackId = active.TrackId;
        active.Complete(await ResolveTrackDurationMs(trackId, oldPositionMs, ct), oldPositionMs);

        if (newDeviceId.HasValue)
            await events.AddAsync(PlayEvent.Begin(userId, newDeviceId.Value, trackId), ct);

        await events.SaveChangesAsync(ct);
    }

    public async Task SessionClearedAsync(Guid userId, int oldPositionMs, CancellationToken ct)
    {
        var active = await events.FindActiveByUserAsync(userId, ct);
        if (active is null) return;

        active.Complete(await ResolveTrackDurationMs(active.TrackId, oldPositionMs, ct), oldPositionMs);
        await events.SaveChangesAsync(ct);
    }

    private async Task<long> ResolveTrackDurationMs(Guid? trackId, int fallbackMs, CancellationToken ct)
    {
        if (!trackId.HasValue) return fallbackMs;
        var track = await tracks.FindByIdAsync(trackId.Value, ct);
        return track is not null ? (long)track.Duration.TotalMilliseconds : fallbackMs;
    }
}
