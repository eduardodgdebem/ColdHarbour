namespace ColdHarbour.Application.Library.Dtos;

public sealed class AlbumSummaryDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public Guid ArtistId { get; init; }
    public int? Year { get; init; }
    public string ImageRef { get; init; } = "";
    public int TrackCount { get; init; }
}
