namespace ColdHarbour.Application.Library.Dtos;

public sealed class ArtistSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public int AlbumCount { get; init; }
}
