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
                Id       = index + 1,
                Name     = t.Title,
                Author   = t.ArtistName,
                AudioRef = t.LocalPath ?? "",
                ImageRef = ""
            })
            .ToList();

        return new PlaylistDto
        {
            Id       = request.Id,
            Name     = "Library",
            ImageRef = "",
            Musics   = musics
        };
    }
}
