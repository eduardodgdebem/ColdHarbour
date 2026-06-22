using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class GetPlaylistQueryHandler : IRequestHandler<GetPlaylistQuery, PlaylistDto>
{
    private readonly ILibraryReadRepository _repo;

    public GetPlaylistQueryHandler(ILibraryReadRepository repo) => _repo = repo;

    public async Task<PlaylistDto> Handle(GetPlaylistQuery request, CancellationToken cancellationToken)
    {
        var tracks = await _repo.GetAllTracksAsync(cancellationToken);

        var musics = tracks
            .Select((t, index) => new MusicDto
            {
                Id = index + 1,
                TrackId = t.Id,
                AlbumId = t.AlbumId,
                Name = t.Title,
                Author = t.ArtistName,
                AudioRef = $"/api/stream/{t.Id}",
                ImageRef = $"/api/artwork/{t.AlbumId}?size=256",
                DurationSeconds = t.Duration.TotalSeconds,
                TrackNumber = t.TrackNumber
            })
            .ToList();

        return new PlaylistDto
        {
            Id = request.Id,
            Name = "Library",
            ImageRef = "",
            Musics = musics
        };
    }
}
