using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Ports;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class SyncLibraryCommandHandler(ILibraryReconciler reconciler) : IRequestHandler<SyncLibraryCommand>
{
    public Task Handle(SyncLibraryCommand request, CancellationToken cancellationToken)
        => reconciler.ApplyAsync(cancellationToken);
}
