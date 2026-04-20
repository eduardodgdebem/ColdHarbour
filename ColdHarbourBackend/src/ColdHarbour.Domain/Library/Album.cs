namespace ColdHarbour.Domain.Library;

public class Album
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = default!;
    public Guid ArtistId { get; private set; }
    public int? Year { get; private set; }
    public string? CoverPath { get; private set; }

    private Album() { }

    public static Album Create(string title, Guid artistId, int? year = null, string? coverPath = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Album title must not be null or whitespace.", nameof(title));

        if (artistId == Guid.Empty)
            throw new ArgumentException("ArtistId must not be empty.", nameof(artistId));

        return new Album
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            ArtistId = artistId,
            Year = year,
            CoverPath = coverPath
        };
    }
}
