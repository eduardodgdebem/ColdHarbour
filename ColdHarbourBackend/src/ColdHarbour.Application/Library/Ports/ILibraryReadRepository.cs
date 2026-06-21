namespace ColdHarbour.Application.Library.Ports;

public interface ILibraryReadRepository
{
    Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AlbumReadModel>> GetAlbumsAsync(CancellationToken ct = default);

    Task<AlbumDetailReadModel?> GetAlbumAsync(Guid albumId, CancellationToken ct = default);

    Task<IReadOnlyList<ArtistReadModel>> GetArtistsAsync(CancellationToken ct = default);

    Task<ArtistDetailReadModel?> GetArtistAsync(Guid artistId, CancellationToken ct = default);
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

public sealed record AlbumReadModel(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    string? CoverArtSha1,
    int TrackCount
);

public sealed record AlbumDetailReadModel(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    string? CoverArtSha1,
    IReadOnlyList<TrackReadModel> Tracks
);

public sealed record ArtistReadModel(
    Guid Id,
    string Name,
    int AlbumCount
);

public sealed record ArtistDetailReadModel(
    Guid Id,
    string Name,
    IReadOnlyList<AlbumReadModel> Albums
);
