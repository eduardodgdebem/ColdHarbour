using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Domain.Library;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Library;

public sealed class TrackRepository(ColdHarbourDbContext db) : ITrackRepository
{
    public Task<Track?> FindByIdAsync(Guid trackId, CancellationToken ct = default)
        => db.Tracks.FirstOrDefaultAsync(t => t.Id == trackId, ct);

    public Task<Track?> FindByAudioSha1Async(string audioSha1, CancellationToken ct = default)
        => db.Tracks.FirstOrDefaultAsync(t => t.AudioSha1 == audioSha1, ct);

    public Task<Artist?> FindArtistByIdAsync(Guid artistId, CancellationToken ct = default)
        => db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct);

    public Task<Artist?> FindArtistByNameAsync(string name, CancellationToken ct = default)
        => db.Artists.FirstOrDefaultAsync(a => a.Name == name, ct);

    public Task<Album?> FindAlbumByArtistAndTitleAsync(Guid artistId, string title, CancellationToken ct = default)
        => db.Albums.FirstOrDefaultAsync(a => a.ArtistId == artistId && a.Title == title, ct);

    public Task<Album?> FindAlbumByIdAsync(Guid albumId, CancellationToken ct = default)
        => db.Albums.FirstOrDefaultAsync(a => a.Id == albumId, ct);

    public Task<int> CountTracksByAlbumIdAsync(Guid albumId, CancellationToken ct = default)
        => db.Tracks.CountAsync(t => t.AlbumId == albumId, ct);

    public Task<int> CountAlbumsByArtistIdAsync(Guid artistId, CancellationToken ct = default)
        => db.Albums.CountAsync(a => a.ArtistId == artistId, ct);

    public async Task AddArtistAsync(Artist artist, CancellationToken ct = default)
        => await db.Artists.AddAsync(artist, ct);

    public async Task AddAlbumAsync(Album album, CancellationToken ct = default)
        => await db.Albums.AddAsync(album, ct);

    public async Task AddTrackAsync(Track track, CancellationToken ct = default)
        => await db.Tracks.AddAsync(track, ct);

    public void RemoveTrack(Track track) => db.Tracks.Remove(track);
    public void RemoveAlbum(Album album) => db.Albums.Remove(album);
    public void RemoveArtist(Artist artist) => db.Artists.Remove(artist);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
