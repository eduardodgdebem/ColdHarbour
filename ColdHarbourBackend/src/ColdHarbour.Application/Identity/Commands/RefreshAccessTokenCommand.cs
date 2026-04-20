using ColdHarbour.Application.Identity.Dtos;
using ColdHarbour.Application.Identity.Helpers;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using FluentValidation;
using MediatR;

namespace ColdHarbour.Application.Identity.Commands;

public sealed record RefreshAccessTokenCommand(string TokenPlaintext, string DeviceId) : IRequest<AuthenticateResult>;

public sealed class RefreshAccessTokenCommandValidator : AbstractValidator<RefreshAccessTokenCommand>
{
    public RefreshAccessTokenCommandValidator()
    {
        RuleFor(x => x.TokenPlaintext).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}

public sealed class RefreshAccessTokenCommandHandler : IRequestHandler<RefreshAccessTokenCommand, AuthenticateResult>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;

    public RefreshAccessTokenCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _tokenService = tokenService;
    }

    public async Task<AuthenticateResult> Handle(RefreshAccessTokenCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = Sha256Helper.Hex(request.TokenPlaintext);
        var token = await _refreshTokenRepository.FindByTokenHashAsync(tokenHash, cancellationToken);

        if (token is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        if (token.IsExpired)
            throw new UnauthorizedAccessException("Refresh token has expired.");

        if (token.IsConsumed)
        {
            await _refreshTokenRepository.RevokeFamilyAsync(token.FamilyId, cancellationToken);
            await _refreshTokenRepository.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Token reuse detected. All sessions have been revoked.");
        }

        var user = await _userRepository.FindByIdAsync(token.UserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("User not found.");

        var newPlaintext = _tokenService.GenerateRefreshTokenPlaintext();
        var newHash = Sha256Helper.Hex(newPlaintext);
        var newTokenId = Guid.NewGuid();
        var newToken = token.Rotate(newTokenId, newHash, DateTimeOffset.UtcNow.AddDays(14));

        await _refreshTokenRepository.SaveChangesAsync(cancellationToken);
        await _refreshTokenRepository.AddAsync(newToken, cancellationToken);
        await _refreshTokenRepository.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, request.DeviceId);
        return new AuthenticateResult(new AuthResultDto(accessToken, user.Id, user.Email), newPlaintext);
    }
}
