using ColdHarbour.Application.Library.Dtos;

namespace ColdHarbour.Application.Library.Ports;

public interface ITrackIngestService
{
    Task<TrackUploadResultDto> IngestAsync(Stream fileStream, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Registers a file that already lives under <c>library/</c> (dropped directly onto the mount)
    /// without moving or copying it. <paramref name="relativePath"/> is the content-root-relative
    /// path, e.g. <c>/library/Artist/Album/track.mp3</c>. Used by library sync reconciliation.
    /// </summary>
    Task<TrackUploadResultDto> IngestExistingFileAsync(string relativePath, CancellationToken ct = default);

    Task RemoveTrackFilesAsync(string? localPath, string audioSha1, CancellationToken ct = default);
}
