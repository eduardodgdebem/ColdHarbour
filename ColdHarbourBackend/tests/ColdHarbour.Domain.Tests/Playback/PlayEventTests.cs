using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Playback;

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
        ev.ListenedMs.Should().Be(0);
        ev.PausedAtUtc.Should().BeNull();
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

    // ── Phase-3: PauseListening / ResumeListening / ListenedMs ───────────────

    [Fact]
    public void PauseListening_OnActiveEvent_AccumulatesSegmentIntoListenedMs()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var segmentStart = ev.SegmentStartedAt;
        var pauseAt = segmentStart.AddSeconds(10);

        ev.PauseListening(pauseAt);

        ev.ListenedMs.Should().BeGreaterThanOrEqualTo(9_000)
            .And.BeLessThanOrEqualTo(11_000,
                because: "listened for ~10 s before pausing");
        ev.PausedAtUtc.Should().Be(pauseAt);
    }

    [Fact]
    public void ResumeListening_OnPausedEvent_StartsNewSegment()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pauseAt = ev.SegmentStartedAt.AddSeconds(5);
        var resumeAt = pauseAt.AddHours(1);

        ev.PauseListening(pauseAt);
        ev.ResumeListening(resumeAt);

        ev.PausedAtUtc.Should().BeNull("event is now active again");
        ev.SegmentStartedAt.Should().Be(resumeAt, "new segment begins at resumeAt");
    }

    [Fact]
    public void PauseListening_CalledTwice_IsIdempotent()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pauseAt = ev.SegmentStartedAt.AddSeconds(5);

        ev.PauseListening(pauseAt);
        var listenedAfterFirstPause = ev.ListenedMs;

        // Second call at a later time — must be a no-op
        ev.PauseListening(pauseAt.AddMinutes(30));

        ev.ListenedMs.Should().Be(listenedAfterFirstPause,
            "ListenedMs must not grow on a double-pause");
        ev.PausedAtUtc.Should().Be(pauseAt, "PausedAtUtc must not change on a double-pause");
    }

    [Fact]
    public void ResumeListening_CalledTwice_IsIdempotent()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pauseAt = ev.SegmentStartedAt.AddSeconds(3);
        var resumeAt = pauseAt.AddMinutes(10);

        ev.PauseListening(pauseAt);
        ev.ResumeListening(resumeAt);
        var segmentAfterResume = ev.SegmentStartedAt;

        // Second call — must be a no-op
        ev.ResumeListening(resumeAt.AddMinutes(5));

        ev.SegmentStartedAt.Should().Be(segmentAfterResume, "SegmentStartedAt must not change on double-resume");
        ev.PausedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Complete_AfterPause_AccumulatesOnlyActiveSegments()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var t0 = ev.SegmentStartedAt;

        // Listen for 10 s, then pause for 1 hour
        ev.PauseListening(t0.AddSeconds(10));

        // Complete 1 hour after the pause (still paused — not resumed)
        var endAt = t0.AddSeconds(10).AddHours(1);
        ev.Complete(durationMs: 3_600_000, positionMs: 3_600_000, nowUtc: endAt);

        ev.ListenedMs.Should().BeGreaterThanOrEqualTo(9_000)
            .And.BeLessThanOrEqualTo(11_000,
                because: "only the 10-s active segment counts; the 1-h pause does not");
        ev.ListenedMs.Should().BeLessThanOrEqualTo(
            (long)(ev.EndedAt!.Value - ev.StartedAt).TotalMilliseconds,
            because: "ListenedMs ≤ wall-clock duration invariant must hold");
    }

    [Fact]
    public void Complete_WithoutPause_ListenedMsEqualsActiveSegment()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var t0 = ev.SegmentStartedAt;
        var endAt = t0.AddSeconds(30);

        ev.Complete(durationMs: 30_000, positionMs: 30_000, nowUtc: endAt);

        ev.ListenedMs.Should().BeGreaterThanOrEqualTo(29_000)
            .And.BeLessThanOrEqualTo(31_000,
                because: "no pause — listened for the full ~30 s");
        ev.ListenedMs.Should().BeLessThanOrEqualTo(
            (long)(ev.EndedAt!.Value - ev.StartedAt).TotalMilliseconds,
            because: "ListenedMs ≤ wall-clock duration invariant must hold");
    }

    [Fact]
    public void ListenedMs_NeverExceedsWallClockDuration()
    {
        var ev = PlayEvent.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var t0 = ev.SegmentStartedAt;

        // Pause and resume multiple times
        ev.PauseListening(t0.AddSeconds(5));
        ev.ResumeListening(t0.AddSeconds(5).AddMinutes(10));
        ev.PauseListening(t0.AddSeconds(5).AddMinutes(10).AddSeconds(3));
        ev.ResumeListening(t0.AddSeconds(5).AddMinutes(10).AddSeconds(3).AddMinutes(5));
        var endAt = t0.AddSeconds(5).AddMinutes(10).AddSeconds(3).AddMinutes(5).AddSeconds(7);

        ev.Complete(durationMs: 60_000, positionMs: 60_000, nowUtc: endAt);

        var wallClockMs = (long)(ev.EndedAt!.Value - ev.StartedAt).TotalMilliseconds;
        ev.ListenedMs.Should().BeLessThanOrEqualTo(wallClockMs,
            "ListenedMs ≤ (EndedAt - StartedAt).TotalMilliseconds invariant must hold");
        ev.ListenedMs.Should().BeGreaterThan(0, "some listening happened");
    }
}
