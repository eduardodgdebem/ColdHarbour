using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Library;

public sealed class LibraryReadRepository : ILibraryReadRepository
{
    private readonly ColdHarbourDbContext _db;

    public LibraryReadRepository(ColdHarbourDbContext db) => _db = db;

    public async Task<IReadOnlyList<TrackReadModel>> GetAllTracksAsync(CancellationToken ct = default)
    {
        return await _db.Tracks
            .Join(_db.Albums, t => t.AlbumId, a => a.Id, (t, a) => new { Track = t, Album = a })
            .Join(_db.Artists, ta => ta.Album.ArtistId, ar => ar.Id, (ta, ar) => new TrackReadModel(
                ta.Track.Id,
                ta.Track.AlbumId,
                ta.Track.Title,
                ar.Name,
                ta.Album.Title,
                ta.Track.Duration,
                ta.Track.LocalPath,
                ta.Track.Format,
                ta.Track.Bitrate,
                ta.Track.TrackNumber))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AlbumReadModel>> GetAlbumsAsync(CancellationToken ct = default)
    {
        return await _db.Albums
            .Join(_db.Artists, a => a.ArtistId, ar => ar.Id, (a, ar) => new { Album = a, Artist = ar })
            .OrderBy(x => x.Artist.Name).ThenBy(x => x.Album.Title)
            .Select(x => new AlbumReadModel(
                x.Album.Id,
                x.Album.Title,
                x.Album.ArtistId,
                x.Artist.Name,
                x.Album.Year,
                x.Album.CoverArtSha1,
                _db.Tracks.Count(t => t.AlbumId == x.Album.Id)))
            .ToListAsync(ct);
    }

    public async Task<AlbumDetailReadModel?> GetAlbumAsync(Guid albumId, CancellationToken ct = default)
    {
        var album = await _db.Albums
            .Join(_db.Artists, a => a.ArtistId, ar => ar.Id, (a, ar) => new { Album = a, Artist = ar })
            .Where(x => x.Album.Id == albumId)
            .Select(x => new
            {
                x.Album.Id,
                x.Album.Title,
                x.Album.ArtistId,
                ArtistName = x.Artist.Name,
                x.Album.Year,
                x.Album.CoverArtSha1
            })
            .FirstOrDefaultAsync(ct);

        if (album is null)
            return null;

        var tracks = await _db.Tracks
            .Where(t => t.AlbumId == albumId)
            .OrderBy(t => t.TrackNumber ?? int.MaxValue).ThenBy(t => t.Title)
            .Select(t => new TrackReadModel(
                t.Id,
                t.AlbumId,
                t.Title,
                album.ArtistName,
                album.Title,
                t.Duration,
                t.LocalPath,
                t.Format,
                t.Bitrate,
                t.TrackNumber))
            .ToListAsync(ct);

        return new AlbumDetailReadModel(
            album.Id,
            album.Title,
            album.ArtistId,
            album.ArtistName,
            album.Year,
            album.CoverArtSha1,
            tracks);
    }

    public async Task<IReadOnlyList<ArtistReadModel>> GetArtistsAsync(CancellationToken ct = default)
    {
        return await _db.Artists
            .OrderBy(ar => ar.Name)
            .Select(ar => new ArtistReadModel(
                ar.Id,
                ar.Name,
                _db.Albums.Count(a => a.ArtistId == ar.Id)))
            .ToListAsync(ct);
    }

    public async Task<ArtistDetailReadModel?> GetArtistAsync(Guid artistId, CancellationToken ct = default)
    {
        var artist = await _db.Artists
            .Where(ar => ar.Id == artistId)
            .Select(ar => new { ar.Id, ar.Name })
            .FirstOrDefaultAsync(ct);

        if (artist is null)
            return null;

        var albums = await _db.Albums
            .Where(a => a.ArtistId == artistId)
            .OrderBy(a => a.Title)
            .Select(a => new AlbumReadModel(
                a.Id,
                a.Title,
                a.ArtistId,
                artist.Name,
                a.Year,
                a.CoverArtSha1,
                _db.Tracks.Count(t => t.AlbumId == a.Id)))
            .ToListAsync(ct);

        return new ArtistDetailReadModel(artist.Id, artist.Name, albums);
    }
}
