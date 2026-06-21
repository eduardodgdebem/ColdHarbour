using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class GetAlbumsQueryHandler(ILibraryReadRepository repo)
    : IRequestHandler<GetAlbumsQuery, IReadOnlyList<AlbumSummaryDto>>
{
    public async Task<IReadOnlyList<AlbumSummaryDto>> Handle(GetAlbumsQuery request, CancellationToken cancellationToken)
    {
        var albums = await repo.GetAlbumsAsync(cancellationToken);
        return albums.Select(LibraryRefs.ToSummary).ToList();
    }
}
