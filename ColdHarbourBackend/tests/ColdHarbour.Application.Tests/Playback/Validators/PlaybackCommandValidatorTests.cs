using ColdHarbour.Application.Playback;
using ColdHarbour.Application.Playback.Commands;
using ColdHarbour.Application.Playback.Validators;
using ColdHarbour.Domain.Playback;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Playback.Validators;

// Phase 5 of WS_PROTOCOL_HARDENING. Validators are pure — no session mutation, no DB.
// They run inside the existing ValidationBehavior pipeline for every hub-dispatched command.
// A throwaway empty session satisfies the (un-validated) Session member of each command.

public sealed class SetQueueCommandValidatorTests
{
    private static readonly PlaybackLimits Limits = new() { MaxQueueSize = 1000 };
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private SetQueueCommandValidator Validator => new(Limits);

    [Fact]
    public void Valid_queue_passes()
    {
        var cmd = new SetQueueCommand(Session, new[] { Guid.NewGuid(), Guid.NewGuid() }, 1, Guid.NewGuid());
        Validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_queue_passes()
    {
        // Empty queue is a no-op at the handler, not a validation error.
        var cmd = new SetQueueCommand(Session, Array.Empty<Guid>(), 0, Guid.NewGuid());
        Validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Queue_over_max_size_fails()
    {
        var tracks = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray();
        var cmd = new SetQueueCommand(Session, tracks, 0, Guid.NewGuid());
        Validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Queue_at_exactly_max_size_passes()
    {
        var tracks = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToArray();
        var cmd = new SetQueueCommand(Session, tracks, 0, Guid.NewGuid());
        Validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_guid_entry_fails()
    {
        var cmd = new SetQueueCommand(Session, new[] { Guid.NewGuid(), Guid.Empty }, 0, Guid.NewGuid());
        Validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_start_index_fails()
    {
        var cmd = new SetQueueCommand(Session, new[] { Guid.NewGuid() }, -1, Guid.NewGuid());
        Validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Start_index_at_count_fails()
    {
        var cmd = new SetQueueCommand(Session, new[] { Guid.NewGuid() }, 1, Guid.NewGuid());
        Validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}

public sealed class SeekCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private readonly SeekCommandValidator _validator = new();

    [Fact]
    public void Non_negative_position_passes()
        => _validator.Validate(new SeekCommand(Session, Guid.NewGuid(), 0)).IsValid.Should().BeTrue();

    [Fact]
    public void Negative_position_fails()
        => _validator.Validate(new SeekCommand(Session, Guid.NewGuid(), -1)).IsValid.Should().BeFalse();
}

public sealed class NextPreviousCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());

    [Fact]
    public void Next_valid_device_passes()
        => new NextTrackCommandValidator().Validate(new NextTrackCommand(Session, Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Next_empty_device_fails()
        => new NextTrackCommandValidator().Validate(new NextTrackCommand(Session, Guid.Empty)).IsValid.Should().BeFalse();

    [Fact]
    public void Previous_valid_device_passes()
        => new PreviousTrackCommandValidator().Validate(new PreviousTrackCommand(Session, Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Previous_empty_device_fails()
        => new PreviousTrackCommandValidator().Validate(new PreviousTrackCommand(Session, Guid.Empty)).IsValid.Should().BeFalse();
}

public sealed class PauseResumeCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());

    [Fact]
    public void Pause_null_device_passes()
        => new PauseCommandValidator().Validate(new PauseCommand(Session, null)).IsValid.Should().BeTrue();

    [Fact]
    public void Pause_valid_device_passes()
        => new PauseCommandValidator().Validate(new PauseCommand(Session, Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Pause_empty_device_fails()
        => new PauseCommandValidator().Validate(new PauseCommand(Session, Guid.Empty)).IsValid.Should().BeFalse();

    [Fact]
    public void Resume_null_device_passes()
        => new ResumeCommandValidator().Validate(new ResumeCommand(Session, null)).IsValid.Should().BeTrue();

    [Fact]
    public void Resume_empty_device_fails()
        => new ResumeCommandValidator().Validate(new ResumeCommand(Session, Guid.Empty)).IsValid.Should().BeFalse();
}

public sealed class StopCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());

    [Fact]
    public void Valid_device_passes()
        => new StopCommandValidator().Validate(new StopCommand(Session, Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Empty_device_fails()
        => new StopCommandValidator().Validate(new StopCommand(Session, Guid.Empty)).IsValid.Should().BeFalse();
}

public sealed class SetRepeatModeCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private readonly SetRepeatModeCommandValidator _validator = new();

    [Theory]
    [InlineData(RepeatMode.Off)]
    [InlineData(RepeatMode.All)]
    [InlineData(RepeatMode.One)]
    public void Defined_mode_passes(RepeatMode mode)
        => _validator.Validate(new SetRepeatModeCommand(Session, mode)).IsValid.Should().BeTrue();

    [Fact]
    public void Undefined_mode_fails()
        => _validator.Validate(new SetRepeatModeCommand(Session, (RepeatMode)99)).IsValid.Should().BeFalse();
}

public sealed class TrackEndedCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private readonly TrackEndedCommandValidator _validator = new();

    [Fact]
    public void Valid_passes()
        => _validator.Validate(new TrackEndedCommand(Session, Guid.NewGuid(), Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Empty_track_fails()
        => _validator.Validate(new TrackEndedCommand(Session, Guid.NewGuid(), Guid.Empty)).IsValid.Should().BeFalse();

    [Fact]
    public void Empty_device_fails()
        => _validator.Validate(new TrackEndedCommand(Session, Guid.Empty, Guid.NewGuid())).IsValid.Should().BeFalse();
}

public sealed class AddToQueueCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private readonly AddToQueueCommandValidator _validator = new();

    [Fact]
    public void Valid_passes()
        => _validator.Validate(new AddToQueueCommand(Session, Guid.NewGuid(), Guid.NewGuid(), null)).IsValid.Should().BeTrue();

    [Fact]
    public void Empty_track_fails()
        => _validator.Validate(new AddToQueueCommand(Session, Guid.NewGuid(), Guid.Empty, null)).IsValid.Should().BeFalse();

    [Fact]
    public void Negative_position_fails()
        => _validator.Validate(new AddToQueueCommand(Session, Guid.NewGuid(), Guid.NewGuid(), -1)).IsValid.Should().BeFalse();

    [Fact]
    public void Non_negative_position_passes()
        => _validator.Validate(new AddToQueueCommand(Session, Guid.NewGuid(), Guid.NewGuid(), 0)).IsValid.Should().BeTrue();
}

public sealed class RemoveFromQueueCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private readonly RemoveFromQueueCommandValidator _validator = new();

    [Fact]
    public void Valid_index_passes()
        => _validator.Validate(new RemoveFromQueueCommand(Session, Guid.NewGuid(), 0)).IsValid.Should().BeTrue();

    [Fact]
    public void Negative_index_fails()
        => _validator.Validate(new RemoveFromQueueCommand(Session, Guid.NewGuid(), -1)).IsValid.Should().BeFalse();
}

public sealed class ReorderQueueCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private readonly ReorderQueueCommandValidator _validator = new();

    [Fact]
    public void Valid_indices_pass()
        => _validator.Validate(new ReorderQueueCommand(Session, Guid.NewGuid(), 0, 1)).IsValid.Should().BeTrue();

    [Fact]
    public void Negative_from_fails()
        => _validator.Validate(new ReorderQueueCommand(Session, Guid.NewGuid(), -1, 1)).IsValid.Should().BeFalse();

    [Fact]
    public void Negative_to_fails()
        => _validator.Validate(new ReorderQueueCommand(Session, Guid.NewGuid(), 0, -1)).IsValid.Should().BeFalse();
}

public sealed class ClearQueueCommandValidatorTests
{
    private static PlaybackSession Session => PlaybackSession.Create(Guid.NewGuid());
    private readonly ClearQueueCommandValidator _validator = new();

    [Fact]
    public void Valid_device_passes()
        => _validator.Validate(new ClearQueueCommand(Session, Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void Empty_device_fails()
        => _validator.Validate(new ClearQueueCommand(Session, Guid.Empty)).IsValid.Should().BeFalse();
}
