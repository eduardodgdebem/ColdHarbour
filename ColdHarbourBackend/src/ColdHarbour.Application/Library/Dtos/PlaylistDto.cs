namespace ColdHarbour.Application.Library.Dtos;

public sealed class PlaylistDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string ImageRef { get; init; } = "";
    public IReadOnlyList<MusicDto> Musics { get; init; } = [];
}
