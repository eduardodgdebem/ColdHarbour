using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using MediatR;

namespace ColdHarbour.Application.Library.Handlers;

public sealed class PreviewLibrarySyncQueryHandler(ILibraryReconciler reconciler)
    : IRequestHandler<PreviewLibrarySyncQuery, LibrarySyncDiffDto>
{
    public Task<LibrarySyncDiffDto> Handle(PreviewLibrarySyncQuery request, CancellationToken cancellationToken)
        => reconciler.PreviewAsync(cancellationToken);
}
