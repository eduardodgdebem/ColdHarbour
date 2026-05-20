namespace ColdHarbour.Infrastructure.Playback;

// Infrastructure-owned read model — no Domain entity needed.
// Materialized weekly by PlaybackStatsJob from PlayEvent rows.
public sealed class PlayStats
{
    public Guid TrackId { get; set; }
    public DateOnly WeekOf { get; set; }
    public int PlayCount { get; set; }
    public long TotalMs { get; set; }
}
