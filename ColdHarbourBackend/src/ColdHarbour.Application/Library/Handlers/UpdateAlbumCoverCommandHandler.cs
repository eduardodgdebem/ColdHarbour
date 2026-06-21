using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Ports;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class UpdateAlbumCoverCommandHandler(ITrackRepository repo, IArtworkService artwork)
    : IRequestHandler<UpdateAlbumCoverCommand>
{
    public async Task Handle(UpdateAlbumCoverCommand request, CancellationToken cancellationToken)
    {
        var album = await repo.FindAlbumByIdAsync(request.AlbumId, cancellationToken)
            ?? throw new KeyNotFoundException($"Album {request.AlbumId} not found.");

        var sha1 = await artwork.SaveSourceAsync(request.Content, request.ContentType, cancellationToken);
        album.UpdateCoverArt(sha1);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
