namespace ColdHarbour.Application.Library.Ports;

public interface ILibraryReadRepository
{
    Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default);
}

public sealed record TrackReadModel(
    Guid Id,
    Guid AlbumId,
    string Title,
    string ArtistName,
    string AlbumTitle,
    TimeSpan Duration,
    string? LocalPath,
    string Format,
    int Bitrate
);
