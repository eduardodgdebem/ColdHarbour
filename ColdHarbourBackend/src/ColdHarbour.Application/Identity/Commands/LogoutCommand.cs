using ColdHarbour.Application.Identity.Helpers;
using ColdHarbour.Application.Identity.Ports;
using FluentValidation;
using MediatR;

namespace ColdHarbour.Application.Identity.Commands;

public sealed record LogoutCommand(string TokenPlaintext) : IRequest;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.TokenPlaintext).NotEmpty();
    }
}

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public LogoutCommandHandler(IRefreshTokenRepository refreshTokenRepository)
    {
        _refreshTokenRepository = refreshTokenRepository;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = Sha256Helper.Hex(request.TokenPlaintext);
        var token = await _refreshTokenRepository.FindByTokenHashAsync(tokenHash, cancellationToken);

        if (token is null)
            return;

        token.Revoke();
        await _refreshTokenRepository.SaveChangesAsync(cancellationToken);
    }
}
