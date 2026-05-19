using System.Text.RegularExpressions;

namespace ColdHarbour.Domain.Library;

public class Album
{
    private static readonly Regex Sha1Regex = new("^[0-9a-f]{40}$", RegexOptions.Compiled);

    public Guid Id { get; private set; }
    public string Title { get; private set; } = default!;
    public Guid ArtistId { get; private set; }
    public int? Year { get; private set; }
    public string? CoverPath { get; private set; }
    public string? CoverArtSha1 { get; private set; }

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

    public void UpdateCoverArt(string? sha1)
    {
        if (sha1 != null && !Sha1Regex.IsMatch(sha1))
            throw new ArgumentException("CoverArtSha1 must be a 40-character lowercase hex string.", nameof(sha1));
        CoverArtSha1 = sha1;
    }
}
