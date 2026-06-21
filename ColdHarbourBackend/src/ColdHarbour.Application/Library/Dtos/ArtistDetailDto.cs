namespace ColdHarbour.Application.Library.Dtos;

public sealed class ArtistDetailDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<AlbumSummaryDto> Albums { get; init; } = [];
}
