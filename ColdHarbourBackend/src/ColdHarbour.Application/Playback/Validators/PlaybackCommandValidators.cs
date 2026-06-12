using ColdHarbour.Application.Playback.Commands;
using FluentValidation;

namespace ColdHarbour.Application.Playback.Validators;

// Phase 5 of WS_PROTOCOL_HARDENING. Validators for the commands dispatched from the WS hub.
// They ride the existing ValidationBehavior<TRequest,TResponse> pipeline (registered in
// DependencyInjection), so writing the validator is the whole job — no plumbing.
// The Session member is the live in-memory aggregate and is never validated here; these only
// guard the untrusted JSON-derived scalars (queue length, indices, positions, device-id shape).

public sealed class SetQueueCommandValidator : AbstractValidator<SetQueueCommand>
{
    public SetQueueCommandValidator(PlaybackLimits limits)
    {
        RuleFor(c => c.TrackIds).NotNull();
        RuleFor(c => c.TrackIds.Count)
            .LessThanOrEqualTo(limits.MaxQueueSize)
            .When(c => c.TrackIds is not null);
        RuleForEach(c => c.TrackIds).NotEqual(Guid.Empty);
        // StartIndex only has to point inside a non-empty queue; an empty queue is a handler no-op.
        RuleFor(c => c.StartIndex)
            .InclusiveBetween(0, int.MaxValue)
            .Must((cmd, idx) => idx < cmd.TrackIds.Count)
            .When(c => c.TrackIds is { Count: > 0 });
    }
}

public sealed class SeekCommandValidator : AbstractValidator<SeekCommand>
{
    public SeekCommandValidator()
    {
        // Upper bound is the current track's duration, which the server clamps; only reject negatives.
        RuleFor(c => c.PositionMs).GreaterThanOrEqualTo(0);
    }
}

public sealed class NextTrackCommandValidator : AbstractValidator<NextTrackCommand>
{
    public NextTrackCommandValidator() => RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
}

public sealed class PreviousTrackCommandValidator : AbstractValidator<PreviousTrackCommand>
{
    public PreviousTrackCommandValidator() => RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
}

public sealed class PauseCommandValidator : AbstractValidator<PauseCommand>
{
    // SenderDeviceId is optional (a null sender acts on the active device); validate it only when present.
    public PauseCommandValidator() =>
        RuleFor(c => c.SenderDeviceId!.Value).NotEqual(Guid.Empty).When(c => c.SenderDeviceId.HasValue);
}

public sealed class ResumeCommandValidator : AbstractValidator<ResumeCommand>
{
    public ResumeCommandValidator() =>
        RuleFor(c => c.SenderDeviceId!.Value).NotEqual(Guid.Empty).When(c => c.SenderDeviceId.HasValue);
}

public sealed class StopCommandValidator : AbstractValidator<StopCommand>
{
    public StopCommandValidator() => RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
}

public sealed class SetRepeatModeCommandValidator : AbstractValidator<SetRepeatModeCommand>
{
    public SetRepeatModeCommandValidator() => RuleFor(c => c.Mode).IsInEnum();
}

public sealed class TrackEndedCommandValidator : AbstractValidator<TrackEndedCommand>
{
    public TrackEndedCommandValidator()
    {
        RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
        RuleFor(c => c.TrackId).NotEqual(Guid.Empty);
    }
}

public sealed class AddToQueueCommandValidator : AbstractValidator<AddToQueueCommand>
{
    public AddToQueueCommandValidator()
    {
        RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
        RuleFor(c => c.TrackId).NotEqual(Guid.Empty);
        RuleFor(c => c.Position!.Value).GreaterThanOrEqualTo(0).When(c => c.Position.HasValue);
    }
}

public sealed class RemoveFromQueueCommandValidator : AbstractValidator<RemoveFromQueueCommand>
{
    public RemoveFromQueueCommandValidator()
    {
        RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
        RuleFor(c => c.Index).GreaterThanOrEqualTo(0);
    }
}

public sealed class ReorderQueueCommandValidator : AbstractValidator<ReorderQueueCommand>
{
    public ReorderQueueCommandValidator()
    {
        RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
        RuleFor(c => c.From).GreaterThanOrEqualTo(0);
        RuleFor(c => c.To).GreaterThanOrEqualTo(0);
    }
}

public sealed class ClearQueueCommandValidator : AbstractValidator<ClearQueueCommand>
{
    public ClearQueueCommandValidator() => RuleFor(c => c.SenderDeviceId).NotEqual(Guid.Empty);
}
