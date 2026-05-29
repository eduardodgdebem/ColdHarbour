using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Playback;

/// <summary>
/// Red-contract tests for the PlayEvent domain methods that Phase 3 will add.
/// All tests in this file are skipped because the methods they reference do not yet
/// exist on <see cref="PlayEvent"/>. Phase 3 will add PauseListening, ResumeListening,
/// ListenedMs, and update Complete to accumulate listened time.
/// </summary>
public sealed class PlayEventTests
{
    // ── existing behaviour (green, regression guard) ──────────────────────────

    [Fact]
    public void Begin_CreatesOpenEventWithCorrectFields()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var trackId = Guid.NewGuid();

        var before = DateTimeOffset.UtcNow;
        var ev = PlayEvent.Begin(userId, deviceId, trackId);
        var after = DateTimeOffset.UtcNow;

        ev.UserId.Should().Be(userId);
        ev.DeviceId.Should().Be(deviceId);
        ev.TrackId.Should().Be(trackId);
        ev.StartedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        ev.EndedAt.Should().BeNull();
        ev.CompletedRatio.Should().BeNull();
    }

    [Fact]
    public void Complete_SetsEndedAtAndCompletedRatio()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var before = DateTimeOffset.UtcNow;

        ev.Complete(durationMs: 60_000, positionMs: 45_000);

        ev.EndedAt.Should().NotBeNull().And.BeOnOrAfter(before);
        ev.CompletedRatio.Should().BeApproximately(0.75, precision: 0.001);
    }

    [Fact]
    public void Complete_WithZeroDuration_SetsRatioToZero()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        ev.Complete(durationMs: 0, positionMs: 0);
        ev.CompletedRatio.Should().Be(0);
    }

    [Fact]
    public void Complete_PositionExceedsDuration_ClampsRatioToOne()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        ev.Complete(durationMs: 30_000, positionMs: 60_000);
        ev.CompletedRatio.Should().Be(1.0);
    }

    // ── Phase-3 methods: PauseListening / ResumeListening / ListenedMs ─────────

    [Fact(Skip = "Phase 3 — PlayEvent.PauseListening not yet added")]
    public void PauseListening_OnActiveEvent_AccumulatesSegmentIntoListenedMs()
    {
        // ARRANGE — event that has been playing for ~10 s.
        // PauseListening must close the current listening segment:
        //   ListenedMs += (now - segmentStart).TotalMilliseconds
        // and set PausedAtUtc = nowUtc.
        // Requires: PlayEvent.PauseListening(DateTime nowUtc) and PlayEvent.ListenedMs.
        Assert.Fail("Phase 3 — PlayEvent.PauseListening not implemented");
    }

    [Fact(Skip = "Phase 3 — PlayEvent.ResumeListening not yet added")]
    public void ResumeListening_OnPausedEvent_StartsNewSegment()
    {
        // ResumeListening must clear PausedAtUtc and start a new listening segment.
        // Requires: PlayEvent.ResumeListening(DateTime nowUtc).
        Assert.Fail("Phase 3 — PlayEvent.ResumeListening not implemented");
    }

    [Fact(Skip = "Phase 3 — PlayEvent.PauseListening idempotency not yet added")]
    public void PauseListening_CalledTwice_IsIdempotent()
    {
        // Calling PauseListening on an already-paused event must be a no-op:
        // ListenedMs must not grow and PausedAtUtc must not change.
        Assert.Fail("Phase 3 — PlayEvent.PauseListening not implemented");
    }

    [Fact(Skip = "Phase 3 — PlayEvent.ResumeListening idempotency not yet added")]
    public void ResumeListening_CalledTwice_IsIdempotent()
    {
        // Calling ResumeListening on an already-active (non-paused) event must be a no-op.
        Assert.Fail("Phase 3 — PlayEvent.ResumeListening not implemented");
    }

    [Fact(Skip = "Phase 3 — PlayEvent.Complete with ListenedMs not yet added")]
    public void Complete_AfterPause_AccumulatesOnlyActiveSegments()
    {
        // Sequence: Begin → play 10 s → Pause → idle 1 hour → Complete
        // ListenedMs must reflect only the 10 s of active listening.
        // Invariant: ListenedMs ≤ (EndedAt - StartedAt).TotalMilliseconds
        Assert.Fail("Phase 3 — PlayEvent.ListenedMs not implemented");
    }

    [Fact(Skip = "Phase 3 — PlayEvent.Complete with ListenedMs not yet added")]
    public void Complete_WithoutPause_ListenedMsEqualsDuration()
    {
        // If the event was never paused, ListenedMs should equal
        // approximately (EndedAt - StartedAt) in milliseconds.
        Assert.Fail("Phase 3 — PlayEvent.ListenedMs not implemented");
    }

    [Fact(Skip = "Phase 3 — ListenedMs invariant not yet enforced")]
    public void ListenedMs_NeverExceedsWallClockDuration()
    {
        // Invariant: ListenedMs ≤ (EndedAt - StartedAt).TotalMilliseconds whenever EndedAt is set.
        Assert.Fail("Phase 3 — PlayEvent.ListenedMs not implemented");
    }
}
