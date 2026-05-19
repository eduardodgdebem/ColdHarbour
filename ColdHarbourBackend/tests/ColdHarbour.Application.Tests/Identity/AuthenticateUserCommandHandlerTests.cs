using ColdHarbour.Application.Identity.Commands;
using ColdHarbour.Application.Identity.Dtos;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Identity;

public class AuthenticateUserCommandHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokenRepo = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();

    private AuthenticateUserCommandHandler CreateHandler() =>
        new(_userRepo, _tokenRepo, _hasher, _tokenService);

    private User CreateUser(string email = "user@example.com") =>
        User.Create(email, "Test User", PasswordHash.From("hashed_password"));

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsAuthResult()
    {
        var user = CreateUser();
        _userRepo.FindByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("Password1!", "hashed_password").Returns(true);
        _tokenService.GenerateRefreshTokenPlaintext().Returns("plaintext_token_32_chars_xxxxxxxxxx");
        _tokenService.GenerateAccessToken(user, "device-1").Returns("access_token");
        _tokenService.GenerateMediaToken(user).Returns("media_token");

        var handler = CreateHandler();
        var result = await handler.Handle(new AuthenticateUserCommand("user@example.com", "Password1!", "device-1"), CancellationToken.None);

        result.Dto.AccessToken.Should().Be("access_token");
        result.Dto.Email.Should().Be("user@example.com");
        result.Dto.UserId.Should().Be(user.Id);
        result.RefreshTokenPlaintext.Should().Be("plaintext_token_32_chars_xxxxxxxxxx");
        result.MediaToken.Should().Be("media_token");
    }

    [Fact]
    public async Task Handle_UnknownEmail_Throws()
    {
        _userRepo.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = CreateHandler();
        var act = () => handler.Handle(new AuthenticateUserCommand("nobody@example.com", "Password1!", "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Invalid credentials*");
    }

    [Fact]
    public async Task Handle_WrongPassword_Throws()
    {
        var user = CreateUser();
        _userRepo.FindByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("wrong", "hashed_password").Returns(false);

        var handler = CreateHandler();
        var act = () => handler.Handle(new AuthenticateUserCommand("user@example.com", "wrong", "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Invalid credentials*");
    }

    [Fact]
    public async Task Handle_StoresRefreshToken()
    {
        var user = CreateUser();
        _userRepo.FindByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("Password1!", "hashed_password").Returns(true);
        _tokenService.GenerateRefreshTokenPlaintext().Returns("plaintext_token_32_chars_xxxxxxxxxx");
        _tokenService.GenerateAccessToken(user, "device-1").Returns("access_token");
        _tokenService.GenerateMediaToken(user).Returns("media_token");

        var handler = CreateHandler();
        await handler.Handle(new AuthenticateUserCommand("user@example.com", "Password1!", "device-1"), CancellationToken.None);

        await _tokenRepo.Received(1).AddAsync(Arg.Is<RefreshToken>(t => t.UserId == user.Id), Arg.Any<CancellationToken>());
        await _tokenRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
