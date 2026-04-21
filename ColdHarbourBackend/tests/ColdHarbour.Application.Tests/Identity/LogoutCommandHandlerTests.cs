using ColdHarbour.Application.Identity.Commands;
using ColdHarbour.Application.Identity.Helpers;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using FluentAssertions;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Identity;

public class LogoutCommandHandlerTests
{
    private readonly IRefreshTokenRepository _tokenRepo = Substitute.For<IRefreshTokenRepository>();

    private LogoutCommandHandler CreateHandler() => new(_tokenRepo);

    private RefreshToken CreateValidToken() =>
        RefreshToken.Create(Guid.NewGuid(), Sha256Helper.Hex("valid_plaintext"), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));

    [Fact]
    public async Task Handle_ValidToken_RevokesIt()
    {
        var token = CreateValidToken();
        _tokenRepo.FindByTokenHashAsync(Sha256Helper.Hex("valid_plaintext"), Arg.Any<CancellationToken>()).Returns(token);

        var handler = CreateHandler();
        await handler.Handle(new LogoutCommand("valid_plaintext"), CancellationToken.None);

        token.IsConsumed.Should().BeTrue();
        await _tokenRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownToken_IsIdempotent()
    {
        _tokenRepo.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        var handler = CreateHandler();
        var act = () => handler.Handle(new LogoutCommand("unknown_plaintext"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _tokenRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
