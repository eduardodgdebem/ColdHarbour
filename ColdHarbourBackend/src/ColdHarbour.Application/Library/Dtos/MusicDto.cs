namespace ColdHarbour.Application.Library.Dtos;

public sealed class MusicDto
{
    public int Id { get; init; }
    public Guid TrackId { get; init; }
    public Guid AlbumId { get; init; }
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string AudioRef { get; init; } = "";
    public string ImageRef { get; init; } = "";
    public double DurationSeconds { get; init; }
    public int? TrackNumber { get; init; }
}
