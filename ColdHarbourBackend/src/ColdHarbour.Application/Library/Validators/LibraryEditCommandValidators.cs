using ColdHarbour.Application.Library.Commands;
using FluentValidation;

namespace ColdHarbour.Application.Library.Validators;

// Edge validation for the library-edit commands (Phase 3 of
// LIBRARY_BROWSE_EDIT_PLAYLIST_MIGRATION). They ride the existing
// ValidationBehavior pipeline; failures surface as ValidationException → 400.

public sealed class UpdateTrackCommandValidator : AbstractValidator<UpdateTrackCommand>
{
    public UpdateTrackCommandValidator()
    {
        RuleFor(c => c.TrackId).NotEmpty();
        RuleFor(c => c.Title).NotEmpty().MaximumLength(500);
        RuleFor(c => c.TrackNumber!.Value)
            .GreaterThanOrEqualTo(0)
            .When(c => c.TrackNumber.HasValue);
    }
}

public sealed class UpdateAlbumCommandValidator : AbstractValidator<UpdateAlbumCommand>
{
    public UpdateAlbumCommandValidator()
    {
        RuleFor(c => c.AlbumId).NotEmpty();
        RuleFor(c => c.Title).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Year!.Value)
            .InclusiveBetween(0, 3000)
            .When(c => c.Year.HasValue);
    }
}

public sealed class RenameArtistCommandValidator : AbstractValidator<RenameArtistCommand>
{
    public RenameArtistCommandValidator()
    {
        RuleFor(c => c.ArtistId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(500);
    }
}

public sealed class UpdateAlbumCoverCommandValidator : AbstractValidator<UpdateAlbumCoverCommand>
{
    public UpdateAlbumCoverCommandValidator()
    {
        RuleFor(c => c.AlbumId).NotEmpty();
        RuleFor(c => c.Content).NotNull();
        RuleFor(c => c.ContentType).NotEmpty();
    }
}
