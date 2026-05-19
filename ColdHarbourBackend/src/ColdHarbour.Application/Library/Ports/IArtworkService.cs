namespace ColdHarbour.Application.Library.Ports;

public interface IArtworkService
{
    Task<string?> GetThumbnailPathAsync(Guid albumId, int size, CancellationToken ct = default);
}
