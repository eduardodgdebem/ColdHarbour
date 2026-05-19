using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Playback;

public sealed class DeviceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly string[] BaseCodecs = ["mp3", "flac", "m4a", "wav"];

    [Fact]
    public void Register_SetsAllProperties()
    {
        var id = Guid.NewGuid();
        var device = Device.Register(id, UserId, "Chrome on macOS", "Mozilla/5.0", BaseCodecs, "opus-128");

        device.Id.Should().Be(id);
        device.UserId.Should().Be(UserId);
        device.Name.Should().Be("Chrome on macOS");
        device.UserAgent.Should().Be("Mozilla/5.0");
        device.SupportedCodecs.Should().BeEquivalentTo(BaseCodecs);
        device.PreferredProfile.Should().Be("opus-128");
        device.BitrateCap.Should().BeNull();
        device.LastSeenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Register_WithBitrateCap_SetsBitrateCap()
    {
        var device = Device.Register(Guid.NewGuid(), UserId, "Mobile", "UA", BaseCodecs, "aac-192", bitrateCap: 128);
        device.BitrateCap.Should().Be(128);
    }

    [Fact]
    public void Register_WithEmptyName_Throws()
    {
        var act = () => Device.Register(Guid.NewGuid(), UserId, "", "UA", BaseCodecs, "opus-128");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Heartbeat_UpdatesLastSeenAtAndCapabilities()
    {
        var device = Device.Register(Guid.NewGuid(), UserId, "Chrome", "OldUA", BaseCodecs, "opus-128");
        var newCodecs = new[] { "mp3", "opus" };

        device.Heartbeat("NewUA", newCodecs, "aac-192", bitrateCap: 256);

        device.UserAgent.Should().Be("NewUA");
        device.SupportedCodecs.Should().BeEquivalentTo(newCodecs);
        device.PreferredProfile.Should().Be("aac-192");
        device.BitrateCap.Should().Be(256);
        device.LastSeenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
