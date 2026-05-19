namespace ColdHarbour.Application.Library.Dtos;

public sealed record LibrarySyncItemDto(string Path, string? Title, string? Artist);

public sealed record LibrarySyncDiffDto(
    IReadOnlyList<LibrarySyncItemDto> Added,
    IReadOnlyList<LibrarySyncItemDto> Missing,
    IReadOnlyList<LibrarySyncItemDto> Renamed);
