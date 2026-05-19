using ColdHarbour.Domain.Library;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Library;

public class TrackTests
{
    private static readonly Guid ValidAlbumId = Guid.NewGuid();
    private const string ValidSha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // 40 lowercase hex chars
    private static readonly TimeSpan ValidDuration = TimeSpan.FromSeconds(210);

    [Fact]
    public void Create_WithValidInputs_SetsAllProperties()
    {
        var track = Track.Create(
            title: "Comfortably Numb",
            albumId: ValidAlbumId,
            duration: ValidDuration,
            provider: "local",
            format: "flac",
            bitrate: 900,
            audioSha1: ValidSha1,
            providerRef: null,
            localPath: "/content/library/Pink Floyd/The Wall/06 - Comfortably Numb.flac");

        track.Id.Should().NotBeEmpty();
        track.Title.Should().Be("Comfortably Numb");
        track.AlbumId.Should().Be(ValidAlbumId);
        track.Duration.Should().Be(ValidDuration);
        track.Provider.Should().Be("local");
        track.Format.Should().Be("flac");
        track.Bitrate.Should().Be(900);
        track.AudioSha1.Should().Be(ValidSha1);
        track.ProviderRef.Should().BeNull();
        track.LocalPath.Should().Be("/content/library/Pink Floyd/The Wall/06 - Comfortably Numb.flac");
    }

    [Fact]
    public void Create_WithWhitespaceTitle_Throws()
    {
        var act = () => Track.Create("   ", ValidAlbumId, ValidDuration, "local", "flac", 320, ValidSha1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithNegativeDuration_Throws()
    {
        var act = () => Track.Create("Title", ValidAlbumId, TimeSpan.FromSeconds(-1), "local", "flac", 320, ValidSha1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithBitrateZeroOrNegative_Throws()
    {
        var actZero = () => Track.Create("Title", ValidAlbumId, ValidDuration, "local", "flac", 0, ValidSha1);
        var actNegative = () => Track.Create("Title", ValidAlbumId, ValidDuration, "local", "flac", -1, ValidSha1);

        actZero.Should().Throw<ArgumentException>();
        actNegative.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithInvalidAudioSha1_Throws()
    {
        var act = () => Track.Create("Title", ValidAlbumId, ValidDuration, "local", "flac", 320, "notahash");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithValidAudioSha1_Succeeds()
    {
        var act = () => Track.Create("Title", ValidAlbumId, ValidDuration, "local", "flac", 320, ValidSha1);

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_WithTrackNumber_SetsTrackNumber()
    {
        var track = Track.Create("Title", ValidAlbumId, ValidDuration, "local", "flac", 320, ValidSha1,
            trackNumber: 3);

        track.TrackNumber.Should().Be(3);
    }

    [Fact]
    public void Create_WithoutTrackNumber_TrackNumberIsNull()
    {
        var track = Track.Create("Title", ValidAlbumId, ValidDuration, "local", "flac", 320, ValidSha1);

        track.TrackNumber.Should().BeNull();
    }
}
