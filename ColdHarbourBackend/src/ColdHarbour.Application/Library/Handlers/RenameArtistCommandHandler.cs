using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Ports;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class RenameArtistCommandHandler(ITrackRepository repo)
    : IRequestHandler<RenameArtistCommand>
{
    public async Task Handle(RenameArtistCommand request, CancellationToken cancellationToken)
    {
        var artist = await repo.FindArtistByIdAsync(request.ArtistId, cancellationToken)
            ?? throw new KeyNotFoundException($"Artist {request.ArtistId} not found.");

        artist.Rename(request.Name);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
