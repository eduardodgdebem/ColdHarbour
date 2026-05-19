using System.Text.RegularExpressions;

namespace ColdHarbour.Domain.Library;

public class Track
{
    private static readonly Regex Sha1Regex = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    public Guid Id { get; private set; }
    public string Title { get; private set; } = default!;
    public Guid AlbumId { get; private set; }
    public int? TrackNumber { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string Provider { get; private set; } = default!;
    public string? ProviderRef { get; private set; }
    public string? LocalPath { get; private set; }
    public string Format { get; private set; } = default!;
    public int Bitrate { get; private set; }
    public string AudioSha1 { get; private set; } = default!;

    private Track() { }

    public static Track Create(
        string title,
        Guid albumId,
        TimeSpan duration,
        string provider,
        string format,
        int bitrate,
        string audioSha1,
        string? providerRef = null,
        string? localPath = null,
        int? trackNumber = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Track title must not be null or whitespace.", nameof(title));

        if (albumId == Guid.Empty)
            throw new ArgumentException("AlbumId must not be empty.", nameof(albumId));

        if (duration < TimeSpan.Zero)
            throw new ArgumentException("Duration must be zero or positive.", nameof(duration));

        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider must not be null or whitespace.", nameof(provider));

        if (string.IsNullOrWhiteSpace(format))
            throw new ArgumentException("Format must not be null or whitespace.", nameof(format));

        if (bitrate <= 0)
            throw new ArgumentException("Bitrate must be greater than zero.", nameof(bitrate));

        if (string.IsNullOrWhiteSpace(audioSha1) || !Sha1Regex.IsMatch(audioSha1))
            throw new ArgumentException("AudioSha1 must be a 40-character lowercase hex string.", nameof(audioSha1));

        return new Track
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            AlbumId = albumId,
            TrackNumber = trackNumber,
            Duration = duration,
            Provider = provider.Trim(),
            ProviderRef = providerRef,
            LocalPath = localPath,
            Format = format.Trim(),
            Bitrate = bitrate,
            AudioSha1 = audioSha1
        };
    }
}
