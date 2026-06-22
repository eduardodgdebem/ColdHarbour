using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;

namespace ColdHarbour.Application.Library.Handlers;

/// <summary>
/// Shared URL/DTO mapping for library read models. The artwork ImageRef carries a
/// <c>?v={coverArtSha1}</c> cache-buster so a re-uploaded cover invalidates the
/// otherwise-immutable artwork cache (see ArtworkController ETag).
/// </summary>
internal static class LibraryRefs
{
    public static string AlbumImageRef(Guid albumId, string? coverArtSha1)
        => coverArtSha1 is { Length: > 0 }
            ? $"/api/artwork/{albumId}?size=256&v={coverArtSha1}"
            : $"/api/artwork/{albumId}?size=256";

    public static AlbumSummaryDto ToSummary(AlbumReadModel a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Artist = a.ArtistName,
        ArtistId = a.ArtistId,
        Year = a.Year,
        ImageRef = AlbumImageRef(a.Id, a.CoverArtSha1),
        TrackCount = a.TrackCount
    };

    public static MusicDto ToMusic(TrackReadModel t, string? coverArtSha1) => new()
    {
        Id = 0,
        TrackId = t.Id,
        AlbumId = t.AlbumId,
        Name = t.Title,
        // Prefer the per-track performer (keeps "feat." credits) over the album artist.
        Author = string.IsNullOrWhiteSpace(t.Performer) ? t.ArtistName : t.Performer,
        AudioRef = $"/api/stream/{t.Id}",
        ImageRef = AlbumImageRef(t.AlbumId, coverArtSha1),
        DurationSeconds = t.Duration.TotalSeconds,
        TrackNumber = t.TrackNumber
    };
}
