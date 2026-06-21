using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Ports;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class UpdateAlbumCommandHandler(ITrackRepository repo)
    : IRequestHandler<UpdateAlbumCommand>
{
    public async Task Handle(UpdateAlbumCommand request, CancellationToken cancellationToken)
    {
        var album = await repo.FindAlbumByIdAsync(request.AlbumId, cancellationToken)
            ?? throw new KeyNotFoundException($"Album {request.AlbumId} not found.");

        album.UpdateMetadata(request.Title, request.Year);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
