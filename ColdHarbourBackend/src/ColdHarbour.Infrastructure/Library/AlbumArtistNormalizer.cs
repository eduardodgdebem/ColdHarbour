using System.Text.RegularExpressions;

namespace ColdHarbour.Infrastructure.Library;

/// <summary>
/// Derives the *album artist* used to group tracks into albums. Tracks with featured
/// performers (e.g. "Daniel Caesar feat. H.E.R.") must collapse onto the base album
/// artist ("Daniel Caesar"), otherwise each feature splits off into its own album.
/// </summary>
public static class AlbumArtistNormalizer
{
    // Matches a trailing feature clause: optional "(" / "[", then feat/ft/featuring/with,
    // then everything to the end. Case-insensitive.
    private static readonly Regex FeatureClause = new(
        @"\s*[\(\[]?\s*\b(feat\.?|ft\.?|featuring|with)\b.*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Unknown Artist";

        var stripped = FeatureClause.Replace(raw, string.Empty);
        // If the whole string was a feature clause, keep the original (trimmed).
        if (string.IsNullOrWhiteSpace(stripped))
            stripped = raw;

        return CollapseWhitespace.Replace(stripped.Trim(), " ").TrimEnd(' ', '(', '[', '-', '–', '—').Trim();
    }
}
