using ColdHarbour.Application.Identity.Commands;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Application.Identity.Helpers;
using ColdHarbour.Domain.Identity;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Identity;

public class RefreshAccessTokenCommandHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokenRepo = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();

    private RefreshAccessTokenCommandHandler CreateHandler() =>
        new(_userRepo, _tokenRepo, _tokenService);

    private User CreateUser() =>
        User.Create("user@example.com", "Test User", PasswordHash.From("hashed"));

    private RefreshToken CreateValidToken(Guid userId) =>
        RefreshToken.Create(userId, Sha256Helper.Hex("valid_plaintext"), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));

    [Fact]
    public async Task Handle_ValidToken_ReturnsNewAuthResult()
    {
        var user = CreateUser();
        var token = CreateValidToken(user.Id);
        _tokenRepo.FindByTokenHashAsync(Sha256Helper.Hex("valid_plaintext"), Arg.Any<CancellationToken>()).Returns(token);
        _userRepo.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _tokenService.GenerateRefreshTokenPlaintext().Returns("new_plaintext_token_xxxxxxxxxx");
        _tokenService.GenerateAccessToken(user, "device-1").Returns("new_access_token");

        var handler = CreateHandler();
        var result = await handler.Handle(new RefreshAccessTokenCommand("valid_plaintext", "device-1"), CancellationToken.None);

        result.Dto.AccessToken.Should().Be("new_access_token");
        result.RefreshTokenPlaintext.Should().Be("new_plaintext_token_xxxxxxxxxx");
    }

    [Fact]
    public async Task Handle_ExpiredToken_Throws()
    {
        var userId = Guid.NewGuid();
        var expiredToken = RefreshToken.CreateExpiredForTesting(userId, Sha256Helper.Hex("expired_plaintext"), Guid.NewGuid());
        _tokenRepo.FindByTokenHashAsync(Sha256Helper.Hex("expired_plaintext"), Arg.Any<CancellationToken>()).Returns(expiredToken);

        var handler = CreateHandler();
        var act = () => handler.Handle(new RefreshAccessTokenCommand("expired_plaintext", "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_ConsumedToken_RevokesFamilyAndThrows()
    {
        var user = CreateUser();
        var token = CreateValidToken(user.Id);
        // Rotate it once to mark it consumed
        token.Rotate(Guid.NewGuid(), Sha256Helper.Hex("some_new_hash"), DateTimeOffset.UtcNow.AddDays(14));
        _tokenRepo.FindByTokenHashAsync(Sha256Helper.Hex("valid_plaintext"), Arg.Any<CancellationToken>()).Returns(token);

        var handler = CreateHandler();
        var act = () => handler.Handle(new RefreshAccessTokenCommand("valid_plaintext", "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Token reuse detected*");
        await _tokenRepo.Received(1).RevokeFamilyAsync(token.FamilyId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownToken_Throws()
    {
        _tokenRepo.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        var handler = CreateHandler();
        var act = () => handler.Handle(new RefreshAccessTokenCommand("unknown_plaintext", "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
