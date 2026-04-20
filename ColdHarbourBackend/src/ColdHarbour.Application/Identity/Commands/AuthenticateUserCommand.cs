using ColdHarbour.Application.Identity.Dtos;
using ColdHarbour.Application.Identity.Helpers;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using FluentValidation;
using MediatR;

namespace ColdHarbour.Application.Identity.Commands;

public sealed record AuthenticateUserCommand(string Email, string Password, string DeviceId) : IRequest<AuthenticateResult>;

public sealed class AuthenticateUserCommandValidator : AbstractValidator<AuthenticateUserCommand>
{
    public AuthenticateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}

public sealed class AuthenticateUserCommandHandler : IRequestHandler<AuthenticateUserCommand, AuthenticateResult>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthenticateUserCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthenticateResult> Handle(AuthenticateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null)
            throw new UnauthorizedAccessException("Invalid credentials.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash.Value))
            throw new UnauthorizedAccessException("Invalid credentials.");

        var plaintext = _tokenService.GenerateRefreshTokenPlaintext();
        var tokenHash = Sha256Helper.Hex(plaintext);
        var refreshToken = RefreshToken.Create(user.Id, tokenHash, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));

        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await _refreshTokenRepository.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, request.DeviceId);
        return new AuthenticateResult(new AuthResultDto(accessToken, user.Id, user.Email), plaintext);
    }
}
