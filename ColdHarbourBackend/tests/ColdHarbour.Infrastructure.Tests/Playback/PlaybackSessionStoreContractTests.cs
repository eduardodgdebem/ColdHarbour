using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Playback;
using FluentAssertions;

namespace ColdHarbour.Infrastructure.Tests.Playback;

/// <summary>
/// Contract (clone-semantics) tests for IPlaybackSessionStore.
/// Every implementation must satisfy these invariants so the actor's
/// "one writer = the actor" rule holds even when the store is swapped.
/// </summary>
public sealed class PlaybackSessionStoreContractTests
{
    // ── invariant 1: LoadAsync returns null for an unknown user ───────────────

    [Fact]
    public async Task LoadAsync_UnknownUser_ReturnsNull()
    {
        var store = new InMemoryPlaybackSessionStore();
        var result = await store.LoadAsync(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeNull("no snapshot has been saved for this user yet");
    }

    // ── invariant 2: round-trip — SaveAsync then LoadAsync returns equivalent session ──

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_ReturnsEquivalentSession()
    {
        var store = new InMemoryPlaybackSessionStore();
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 1);
        session.ClaimActiveIfNone(device);
        session.Seek(42_000);
        session.SetRepeatMode(RepeatMode.All);
        session.SetShuffle(true);

        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var loaded = await store.LoadAsync(userId, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.UserId.Should().Be(userId);
        loaded.ActiveDeviceId.Should().Be(device);
        loaded.TrackId.Should().Be(tracks[1]);
        loaded.PositionMs.Should().Be(42_000);
        loaded.IsPlaying.Should().BeTrue();
        loaded.Queue.Should().Equal(tracks);
        loaded.QueueIndex.Should().Be(1);
        loaded.RepeatMode.Should().Be(RepeatMode.All);
        loaded.Shuffle.Should().BeTrue();
    }

    // ── invariant 3: mutating the loaded session does not affect subsequent LoadAsync ──

    [Fact]
    public async Task MutatingLoadedSession_DoesNotAffect_SubsequentLoad()
    {
        var store = new InMemoryPlaybackSessionStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var session = PlaybackSession.Create(userId);
        session.SetQueue(tracks, 0);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var first = await store.LoadAsync(userId, CancellationToken.None);
        first!.Seek(99_000); // mutate the loaded snapshot

        var second = await store.LoadAsync(userId, CancellationToken.None);
        second!.PositionMs.Should().Be(0,
            "mutating a loaded snapshot must not affect the stored state");
    }

    // ── invariant 4: each LoadAsync returns a distinct instance ──────────────

    [Fact]
    public async Task LoadAsync_ReturnsFreshInstance_EachTime()
    {
        var store = new InMemoryPlaybackSessionStore();
        var userId = Guid.NewGuid();

        var session = PlaybackSession.Create(userId);
        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var a = await store.LoadAsync(userId, CancellationToken.None);
        var b = await store.LoadAsync(userId, CancellationToken.None);

        a.Should().NotBeSameAs(b,
            "each LoadAsync must return an independent clone");
    }

    // ── invariant 4b: revision survives round-trip ────────────────────────────

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_PreservesRevision()
    {
        var store = new InMemoryPlaybackSessionStore();
        var userId = Guid.NewGuid();

        var session = PlaybackSession.Create(userId);
        session.IncrementRevision();
        session.IncrementRevision();
        session.IncrementRevision(); // revision == 3

        await store.SaveAsync(session, SaveReason.MaterialChange, CancellationToken.None);

        var loaded = await store.LoadAsync(userId, CancellationToken.None);
        loaded!.Revision.Should().Be(3, "revision must survive a save/load round-trip");
    }

    // ── invariant 5: last-writer-wins (documented known limitation) ──────────

    [Fact]
    public async Task SaveAsync_LastWrite_Wins()
    {
        // Two "actors" saving concurrently — last write wins (no optimistic concurrency).
        // This is a documented limitation until a Redis-with-optimistic-concurrency phase.
        var store = new InMemoryPlaybackSessionStore();
        var userId = Guid.NewGuid();
        var tracks = new[] { Guid.NewGuid() };

        var sessionA = PlaybackSession.Create(userId);
        sessionA.SetQueue(tracks, 0);
        sessionA.Seek(1_000);

        var sessionB = PlaybackSession.Create(userId);
        sessionB.SetQueue(tracks, 0);
        sessionB.Seek(2_000);

        await store.SaveAsync(sessionA, SaveReason.MaterialChange, CancellationToken.None);
        await store.SaveAsync(sessionB, SaveReason.MaterialChange, CancellationToken.None);

        var loaded = await store.LoadAsync(userId, CancellationToken.None);
        loaded!.PositionMs.Should().Be(2_000, "last write wins");
    }
}
