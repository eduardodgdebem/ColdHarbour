using ColdHarbour.Application.Library.Dtos;

namespace ColdHarbour.Application.Library.Ports;

public interface ILibraryReconciler
{
    Task<LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default);
    Task ApplyAsync(CancellationToken ct = default);
}
