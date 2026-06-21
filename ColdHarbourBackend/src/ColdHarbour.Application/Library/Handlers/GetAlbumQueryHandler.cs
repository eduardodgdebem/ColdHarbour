using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class GetAlbumQueryHandler(ILibraryReadRepository repo)
    : IRequestHandler<GetAlbumQuery, AlbumDetailDto?>
{
    public async Task<AlbumDetailDto?> Handle(GetAlbumQuery request, CancellationToken cancellationToken)
    {
        var album = await repo.GetAlbumAsync(request.AlbumId, cancellationToken);
        if (album is null)
            return null;

        return new AlbumDetailDto
        {
            Id = album.Id,
            Title = album.Title,
            Artist = album.ArtistName,
            ArtistId = album.ArtistId,
            Year = album.Year,
            ImageRef = LibraryRefs.AlbumImageRef(album.Id, album.CoverArtSha1),
            Tracks = album.Tracks.Select(t => LibraryRefs.ToMusic(t, album.CoverArtSha1)).ToList()
        };
    }
}
