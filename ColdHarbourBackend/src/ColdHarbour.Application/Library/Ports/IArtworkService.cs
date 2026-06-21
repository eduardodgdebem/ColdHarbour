namespace ColdHarbour.Application.Library.Ports;

public interface IArtworkService
{
    Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default);

    /// <summary>
    /// The current cover-art sha1 for an album, or null if it has no cover. Used to
    /// version the artwork ETag so a re-uploaded cover invalidates cached responses.
    /// </summary>
    Task<string?> GetCoverArtSha1Async(Guid albumId, CancellationToken ct = default);

    /// <summary>
    /// Validate an uploaded image (MIME + magic bytes + size), write it into
    /// cache/art/{sha1}-source.{ext} (never library/), regenerate the thumbnail
    /// sizes, and return the content sha1. Throws <see cref="InvalidOperationException"/>
    /// for non-image, oversize, or mismatched-magic-byte payloads.
    /// </summary>
    Task<string> SaveSourceAsync(Stream content, string contentType, CancellationToken ct = default);
}
