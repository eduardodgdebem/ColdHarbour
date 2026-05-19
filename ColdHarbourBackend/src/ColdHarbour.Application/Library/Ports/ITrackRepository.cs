using ColdHarbour.Domain.Library;

namespace ColdHarbour.Application.Library.Ports;

public interface ITrackRepository
{
    Task<Track?> FindByIdAsync(Guid trackId, CancellationToken ct = default);
    Task<Track?> FindByAudioSha1Async(string audioSha1, CancellationToken ct = default);
    Task<Artist?> FindArtistByIdAsync(Guid artistId, CancellationToken ct = default);
    Task<Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default);
    Task<Album?> FindAlbumByArtistAndTitleAsync(Guid artistId, string title, CancellationToken ct = default);
    Task<Album?> FindAlbumByIdAsync(Guid albumId, CancellationToken ct = default);
    Task<int> CountTracksByAlbumIdAsync(Guid albumId, CancellationToken ct = default);
    Task<int> CountAlbumsByArtistIdAsync(Guid artistId, CancellationToken ct = default);
    Task AddArtistAsync(Artist artist, CancellationToken ct = default);
    Task AddAlbumAsync(Album album, CancellationToken ct = default);
    Task AddTrackAsync(Track track, CancellationToken ct = default);
    void RemoveTrack(Track track);
    void RemoveAlbum(Album album);
    void RemoveArtist(Artist artist);
    Task SaveChangesAsync(CancellationToken ct = default);
}
