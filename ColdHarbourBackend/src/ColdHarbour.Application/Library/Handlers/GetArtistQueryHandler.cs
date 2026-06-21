using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class GetArtistQueryHandler(ILibraryReadRepository repo)
    : IRequestHandler<GetArtistQuery, ArtistDetailDto?>
{
    public async Task<ArtistDetailDto?> Handle(GetArtistQuery request, CancellationToken cancellationToken)
    {
        var artist = await repo.GetArtistAsync(request.ArtistId, cancellationToken);
        if (artist is null)
            return null;

        return new ArtistDetailDto
        {
            Id = artist.Id,
            Name = artist.Name,
            Albums = artist.Albums.Select(LibraryRefs.ToSummary).ToList()
        };
    }
}
