namespace ColdHarbour.Application.Library.Dtos;

public sealed class MusicDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string AudioRef { get; init; } = "";
    public string ImageRef { get; init; } = "";
}
