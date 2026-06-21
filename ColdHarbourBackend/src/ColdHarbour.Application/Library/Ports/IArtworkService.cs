namespace ColdHarbour.Application.Library.Ports;

public interface IArtworkService
{
    Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default);

    /// <summary>
    /// The current cover-art sha1 for an album, or null if it has no cover. Used to
    /// version the artwork ETag so a re-uploaded cover invalidates cached responses.
    /// </summary>
    Task<string?> GetCoverArtSha1Async(Guid albumId, CancellationToken ct = default);
}
