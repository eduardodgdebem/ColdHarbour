using ColdHarbour.Api.Playback;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Phase 3 of WS_PROTOCOL_HARDENING. Two contained guards on the per-user actor:
///   (A) IsActiveDevice fails closed — it no longer returns true for a missing
///       (Guid.Empty) device or when no device owns playback yet (null active).
///   (B) SenderDeviceToValidate names which inbound commands carry a claiming/transport
///       device that must be proven to belong to the user before dispatch. Heartbeat and
///       stop are exempt (already covered by IsActiveDevice); read-only / device-less
///       commands are exempt too.
/// </summary>
public sealed class PlaybackUserActorGuardTests
{
    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static PlaybackSession SessionWithActive(Guid? active)
    {
        var session = PlaybackSession.Create(Guid.NewGuid());
        if (active is { } a) session.ClaimActiveIfNone(a);
        return session;
    }

    // ── Part A: IsActiveDevice fails closed ─────────────────────────────────────

    [Fact]
    public void IsActiveDevice_false_when_no_device_owns_playback_yet()
        => PlaybackUserActor.IsActiveDevice(SessionWithActive(null), DeviceA).Should().BeFalse();

    [Fact]
    public void IsActiveDevice_true_for_the_current_active_device()
        => PlaybackUserActor.IsActiveDevice(SessionWithActive(DeviceA), DeviceA).Should().BeTrue();

    [Fact]
    public void IsActiveDevice_false_for_a_different_device()
        => PlaybackUserActor.IsActiveDevice(SessionWithActive(DeviceA), DeviceB).Should().BeFalse();

    [Fact]
    public void IsActiveDevice_false_for_an_empty_device_id()
        => PlaybackUserActor.IsActiveDevice(SessionWithActive(DeviceA), Guid.Empty).Should().BeFalse();

    // ── Part B: which commands get sender validation ───────────────────────────

    [Fact]
    public void SenderDeviceToValidate_returns_the_device_for_transport_commands()
    {
        PlaybackUserActor.SenderDeviceToValidate(new SetQueueCmd(DeviceA, [Guid.NewGuid()], 0)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new NextCmd(DeviceA)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new PreviousCmd(DeviceA)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new SeekCmd(DeviceA, 1000)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new TransferCmd(DeviceA, 0)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new AddToQueueCmd(DeviceA, Guid.NewGuid(), null)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new RemoveFromQueueCmd(DeviceA, 0)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new ReorderQueueCmd(DeviceA, 0, 1)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new ClearQueueCmd(DeviceA)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new TrackEndedCmd(DeviceA, Guid.NewGuid(), 0)).Should().Be(DeviceA);
    }

    [Fact]
    public void SenderDeviceToValidate_returns_the_device_for_pause_resume_only_when_provided()
    {
        PlaybackUserActor.SenderDeviceToValidate(new PauseCmd(DeviceA)).Should().Be(DeviceA);
        PlaybackUserActor.SenderDeviceToValidate(new ResumeCmd(DeviceA)).Should().Be(DeviceA);
        // Pause/resume without a device are valid (they act on the active device) — nothing to validate.
        PlaybackUserActor.SenderDeviceToValidate(new PauseCmd(null)).Should().BeNull();
        PlaybackUserActor.SenderDeviceToValidate(new ResumeCmd(null)).Should().BeNull();
    }

    [Fact]
    public void SenderDeviceToValidate_exempts_heartbeat_stop_resync_and_deviceless_commands()
    {
        // Heartbeat + stop are already gated by IsActiveDevice; resync is read-only.
        PlaybackUserActor.SenderDeviceToValidate(new HeartbeatCmd(DeviceA, 0)).Should().BeNull();
        PlaybackUserActor.SenderDeviceToValidate(new StopCmd(DeviceA)).Should().BeNull();
        PlaybackUserActor.SenderDeviceToValidate(new ResyncCmd(DeviceA, 0)).Should().BeNull();
        PlaybackUserActor.SenderDeviceToValidate(new SetRepeatModeCmd(RepeatMode.All)).Should().BeNull();
        PlaybackUserActor.SenderDeviceToValidate(new SetShuffleCmd(true)).Should().BeNull();
    }
}
