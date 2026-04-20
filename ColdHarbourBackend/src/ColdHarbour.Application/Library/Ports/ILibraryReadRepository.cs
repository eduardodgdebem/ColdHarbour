namespace ColdHarbour.Application.Library.Ports;

public interface ILibraryReadRepository
{
    Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default);
}

public sealed record TrackReadModel(
    Guid Id,
    string Title,
    string ArtistName,
    string? LocalPath,
    string Format,
    int Bitrate);
