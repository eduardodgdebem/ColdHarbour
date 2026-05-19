using ColdHarbour.Application.Library.Dtos;

namespace ColdHarbour.Application.Library.Ports;

public interface ITrackIngestService
{
    Task<TrackUploadResultDto> IngestAsync(Stream fileStream, string fileName, CancellationToken ct = default);
    Task RemoveTrackFilesAsync(string? localPath, string audioSha1, CancellationToken ct = default);
}
