namespace ColdHarbour.Infrastructure.Playback;

/// <summary>
/// EF Core persistence model for a <see cref="Domain.Playback.PlaybackSession"/> snapshot.
/// One row per user. Written by <see cref="PostgresPlaybackSessionStore"/>;
/// never exposed outside <c>ColdHarbour.Infrastructure</c>.
/// </summary>
public sealed class PlaybackSessionSnapshot
{
    public Guid UserId { get; set; }
    public Guid? ActiveDeviceId { get; set; }
    public Guid? TrackId { get; set; }
    public long PositionMs { get; set; }
    public bool IsPlaying { get; set; }

    /// <summary>
    /// Stored as a <c>jsonb</c> column containing a JSON array of UUID strings.
    /// The store is responsible for serialising/deserialising <c>List&lt;Guid&gt;</c>
    /// to/from this string so that Npgsql's generic type-mapping is not required.
    /// </summary>
    public string QueueJson { get; set; } = "[]";

    public int QueueIndex { get; set; }

    /// <summary>Stored as a lowercase string: "off" | "all" | "one".</summary>
    public string RepeatMode { get; set; } = "off";

    public bool Shuffle { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }
}
