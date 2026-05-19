using ColdHarbour.Application.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback;

public sealed class ProfileSelectorTests
{
    [Fact]
    public void Select_WhenFormatSupportedAndNoBitrateCap_ReturnsOriginal()
    {
        var profile = ProfileSelector.Select("flac", ["mp3", "flac", "m4a"], "opus-128", bitrateCap: null);
        profile.Should().Be("original");
    }

    [Fact]
    public void Select_WhenFormatSupportedButBitrateCapSet_ReturnsPreferred()
    {
        var profile = ProfileSelector.Select("mp3", ["mp3", "flac"], "opus-128", bitrateCap: 128);
        profile.Should().Be("opus-128");
    }

    [Fact]
    public void Select_WhenFormatNotSupported_ReturnsPreferredProfile()
    {
        var profile = ProfileSelector.Select("ogg", ["mp3", "m4a"], "aac-192", bitrateCap: null);
        profile.Should().Be("aac-192");
    }

    [Fact]
    public void Select_CaseInsensitiveFormatMatch()
    {
        var profile = ProfileSelector.Select("FLAC", ["flac", "mp3"], "opus-128", bitrateCap: null);
        profile.Should().Be("original");
    }

    [Fact]
    public void Select_WhenNoDeviceInfo_FallsBackToMp3192()
    {
        var profile = ProfileSelector.Select("flac", [], preferredProfile: null, bitrateCap: null);
        profile.Should().Be("mp3-192");
    }
}
