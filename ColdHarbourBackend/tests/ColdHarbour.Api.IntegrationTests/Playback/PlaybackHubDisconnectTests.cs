using ColdHarbour.Api.Playback;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Verifies the playback session survives a device disconnecting.
///
/// A page refresh closes the WebSocket; the hub's disconnect path used to call
/// <c>session.Clear()</c> whenever the disconnecting socket belonged to the active
/// device — wiping TrackId / Queue / IsPlaying on every refresh. The decision is
/// extracted into <see cref="PlaybackSessionHub.ApplyDisconnectPolicy"/> so the
/// "durable session" guarantee (Phase 5) can be asserted without driving the
/// TestServer WebSocket lifecycle.
/// </summary>
public sealed class PlaybackHubDisconnectTests
{
    /// <summary>
    /// When the active device disconnects (e.g. a page refresh), the session must
    /// retain its track, queue and playing state so the reconnecting device can
    /// resume. Only an explicit <c>stop</c> message clears the session.
    /// </summary>
    [Fact]
    public void ApplyDisconnectPolicy_KeepsSession_WhenActiveDeviceDisconnects()
    {
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var firstTrack = Guid.NewGuid();
        var session = PlaybackSession.Create(userId);
        session.ClaimActiveIfNone(device);
        session.SetQueue([firstTrack, Guid.NewGuid()], startIndex: 0);

        PlaybackSessionHub.ApplyDisconnectPolicy(session, device);

        session.TrackId.Should().Be(firstTrack, because: "a refresh must not wipe what was playing");
        session.Queue.Should().HaveCount(2, because: "the queue is durable across reconnects");
        session.IsPlaying.Should().BeTrue(because: "the reconnecting device should resume playback");
        session.ActiveDeviceId.Should().Be(device, because: "the same device reclaims active on reconnect");
    }
}
