using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Ports;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using FluentAssertions;
using MediatR;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Playback;

/// <summary>
/// Playback hardening Phase 3. Fires a long, seed-deterministic sequence of random transport
/// commands from several simulated devices through the *real* command handlers and asserts the
/// PlaybackSession invariants hold afterwards. The actor serializes commands per user, so this
/// drives them sequentially (the realistic post-actor model) while still exercising arbitrary
/// interleavings of which device acts. Each seed is reproducible from its InlineData value alone.
/// </summary>
public sealed class ConcurrentCommandPropertyTests
{
    private static readonly Guid[] Devices =
    {
        Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
        Guid.Parse("dddddddd-0000-0000-0000-000000000002"),
        Guid.Parse("dddddddd-0000-0000-0000-000000000003"),
    };

    private static readonly Guid[] TrackPool =
        Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task RandomCommandSequence_PreservesSessionInvariants(int seed)
    {
        var rng = new Random(seed);
        var session = PlaybackSession.Create(Guid.NewGuid());

        var timeline = Substitute.For<IPlaySessionTimeline>();
        var tracks = Substitute.For<ITrackRepository>();
        tracks.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Track?>(null));

        var dispatch = BuildDispatchers(timeline, tracks);

        for (var i = 0; i < 60; i++)
        {
            var device = Devices[rng.Next(Devices.Length)];
            var command = BuildRandomCommand(rng, session, device);
            try
            {
                await dispatch(command);
            }
            catch (QueueTooLargeException)
            {
                // Realistic: the actor drops these. The cap invariant is asserted below.
            }

            AssertInvariants(session, seed, i);
        }

        AssertInvariants(session, seed, 60);
    }

    public static IEnumerable<object[]> Seeds() =>
        Enumerable.Range(0, 50).Select(s => new object[] { s });

    private static void AssertInvariants(PlaybackSession s, int seed, int step)
    {
        var ctx = $"seed={seed} step={step}";

        if (s.Queue.Count == 0)
        {
            s.TrackId.Should().BeNull($"empty queue implies no current track ({ctx})");
            s.IsPlaying.Should().BeFalse($"empty queue implies not playing ({ctx})");
        }
        else
        {
            s.QueueIndex.Should().BeInRange(0, s.Queue.Count - 1, $"QueueIndex must address the queue ({ctx})");
        }

        if (s.TrackId is { } trackId)
            trackId.Should().Be(s.Queue[s.QueueIndex], $"TrackId must equal Queue[QueueIndex] ({ctx})");

        s.Queue.Count.Should().BeLessThanOrEqualTo(PlaybackSession.MaxQueueSize, $"queue cap holds ({ctx})");

        if (s.ActiveDeviceId is { } active)
            Devices.Should().Contain(active, $"ActiveDeviceId must be a known device ({ctx})");
    }

    // ── command construction ──────────────────────────────────────────────────

    private static IRequest<bool> BuildRandomCommand(Random rng, PlaybackSession session, Guid device)
    {
        // Weighted toward setQueue/add so the queue actually grows during the run.
        return rng.Next(13) switch
        {
            0 or 1 => new SetQueueCommand(session, RandomTracks(rng, out var start), start, device),
            2 => new NextTrackCommand(session, device),
            3 => new PreviousTrackCommand(session, device),
            4 => new SeekCommand(session, device, rng.Next(0, 300_000)),
            5 => new PauseCommand(session, device),
            6 => new ResumeCommand(session, device),
            7 or 8 => new AddToQueueCommand(session, device, TrackPool[rng.Next(TrackPool.Length)],
                rng.Next(2) == 0 ? null : rng.Next(0, Math.Max(1, session.Queue.Count + 1))),
            9 => new RemoveFromQueueCommand(session, device, rng.Next(0, Math.Max(1, session.Queue.Count + 2))),
            10 => new ReorderQueueCommand(session, device,
                rng.Next(0, Math.Max(1, session.Queue.Count + 1)),
                rng.Next(0, Math.Max(1, session.Queue.Count + 1))),
            11 => new SetShuffleCommand(session, rng.Next(2) == 0),
            _ => new SetRepeatModeCommand(session, (RepeatMode)rng.Next(0, 3)),
        };
    }

    private static IReadOnlyList<Guid> RandomTracks(Random rng, out int startIndex)
    {
        var count = rng.Next(1, 6);
        var list = Enumerable.Range(0, count).Select(_ => TrackPool[rng.Next(TrackPool.Length)]).ToList();
        startIndex = rng.Next(0, count); // always valid for the generated length
        return list;
    }

    // ── dispatch: route each command record to its real handler ───────────────

    private static Func<IRequest<bool>, Task<bool>> BuildDispatchers(IPlaySessionTimeline timeline, ITrackRepository tracks)
    {
        return command => command switch
        {
            SetQueueCommand c => new SetQueueCommandHandler(timeline).Handle(c, default),
            NextTrackCommand c => new NextTrackCommandHandler(timeline).Handle(c, default),
            PreviousTrackCommand c => new PreviousTrackCommandHandler(timeline).Handle(c, default),
            SeekCommand c => new SeekCommandHandler().Handle(c, default),
            PauseCommand c => new PauseCommandHandler(timeline).Handle(c, default),
            ResumeCommand c => new ResumeCommandHandler(timeline).Handle(c, default),
            AddToQueueCommand c => new AddToQueueCommandHandler(timeline).Handle(c, default),
            RemoveFromQueueCommand c => new RemoveFromQueueCommandHandler(timeline).Handle(c, default),
            ReorderQueueCommand c => new ReorderQueueCommandHandler().Handle(c, default),
            SetShuffleCommand c => new SetShuffleCommandHandler().Handle(c, default),
            SetRepeatModeCommand c => new SetRepeatModeCommandHandler().Handle(c, default),
            _ => throw new InvalidOperationException($"Unhandled command {command.GetType().Name}"),
        };
    }
}
