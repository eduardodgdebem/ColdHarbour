using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Ports;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class UpdateTrackCommandHandler(ITrackRepository repo)
    : IRequestHandler<UpdateTrackCommand>
{
    public async Task Handle(UpdateTrackCommand request, CancellationToken cancellationToken)
    {
        var track = await repo.FindByIdAsync(request.TrackId, cancellationToken)
            ?? throw new KeyNotFoundException($"Track {request.TrackId} not found.");

        track.UpdateMetadata(request.Title, request.TrackNumber);
        await repo.SaveChangesAsync(cancellationToken);
    }
}
