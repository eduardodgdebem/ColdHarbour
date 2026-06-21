using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class GetArtistsQueryHandler(ILibraryReadRepository repo)
    : IRequestHandler<GetArtistsQuery, IReadOnlyList<ArtistSummaryDto>>
{
    public async Task<IReadOnlyList<ArtistSummaryDto>> Handle(GetArtistsQuery request, CancellationToken cancellationToken)
    {
        var artists = await repo.GetArtistsAsync(cancellationToken);
        return artists
            .Select(a => new ArtistSummaryDto { Id = a.Id, Name = a.Name, AlbumCount = a.AlbumCount })
            .ToList();
    }
}
