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
                ta.Track.Title,
                ar.Name,
                ta.Track.LocalPath,
                ta.Track.Format,
                ta.Track.Bitrate))
            .ToListAsync(ct);
    }
}
