using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Queries;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class GetPlaylistQueryHandler : IRequestHandler<GetPlaylistQuery, PlaylistDto>
{
    private static readonly IReadOnlyList<MusicDto> MockMusics =
    [
        new MusicDto
        {
            Id       = 1,
            Name     = "Baby You're Bad",
            Author   = "HONNE",
            AudioRef = "/assets/music/babyyourebad.mp3",
            ImageRef = "/assets/images/babyyourebad.jpg"
        },
        new MusicDto
        {
            Id       = 2,
            Name     = "Liz",
            Author   = "Remi Wolf",
            AudioRef = "/assets/music/liz.mp3",
            ImageRef = "/assets/images/liz.jpg"
        }
    ];

    public Task<PlaylistDto> Handle(GetPlaylistQuery request, CancellationToken cancellationToken)
    {
        var playlist = new PlaylistDto
        {
            Id       = request.Id,
            Name     = "Here we go again",
            ImageRef = "/assets/images/playlist1.jpg",
            Musics   = MockMusics
        };

        return Task.FromResult(playlist);
    }
}
