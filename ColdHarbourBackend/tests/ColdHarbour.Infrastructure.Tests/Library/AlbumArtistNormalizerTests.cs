using ColdHarbour.Infrastructure.Library;
using FluentAssertions;

namespace ColdHarbour.Infrastructure.Tests.Library;

public sealed class AlbumArtistNormalizerTests
{
    [Theory]
    [InlineData("Daniel Caesar feat. H.E.R.", "Daniel Caesar")]
    [InlineData("Daniel Caesar feat. Charlotte Day Wilson", "Daniel Caesar")]
    [InlineData("Daniel Caesar ft. Syd", "Daniel Caesar")]
    [InlineData("Daniel Caesar featuring Kali Uchis", "Daniel Caesar")]
    [InlineData("Daniel Caesar (feat. Syd)", "Daniel Caesar")]
    [InlineData("Daniel Caesar [feat. Syd]", "Daniel Caesar")]
    public void Normalize_StripsFeatureClause(string raw, string expected)
    {
        AlbumArtistNormalizer.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("Daniel Caesar", "Daniel Caesar")]
    [InlineData("  Daniel Caesar  ", "Daniel Caesar")]
    [InlineData("Earth, Wind & Fire", "Earth, Wind & Fire")] // '&' is part of the name, not a feature
    public void Normalize_LeavesPlainNamesIntact(string raw, string expected)
    {
        AlbumArtistNormalizer.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankBecomesUnknownArtist(string? raw)
    {
        AlbumArtistNormalizer.Normalize(raw).Should().Be("Unknown Artist");
    }

    [Fact]
    public void Normalize_CollapsesInnerWhitespace()
    {
        AlbumArtistNormalizer.Normalize("Daniel    Caesar").Should().Be("Daniel Caesar");
    }
}
