using ColdHarbour.Application.Identity.Commands;
using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;

namespace ColdHarbour.Application.Tests.Identity;

public class RegisterUserCommandHandlerTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();

    private RegisterUserCommandHandler CreateHandler() =>
        new(_userRepo, _hasher);

    [Fact]
    public async Task Handle_FirstUser_CreatesUser()
    {
        _userRepo.AnyUsersExistAsync(Arg.Any<CancellationToken>()).Returns(false);
        _hasher.Hash("Password1!").Returns("hashed");

        var handler = CreateHandler();
        await handler.Handle(new RegisterUserCommand("test@example.com", "Test User", "Password1!"), CancellationToken.None);

        await _userRepo.Received(1).AddAsync(Arg.Is<User>(u => u.Email == "test@example.com"), Arg.Any<CancellationToken>());
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUsersExist_Throws()
    {
        _userRepo.AnyUsersExistAsync(Arg.Any<CancellationToken>()).Returns(true);

        var handler = CreateHandler();
        var act = () => handler.Handle(new RegisterUserCommand("test@example.com", "Test User", "Password1!"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Registration is closed*");
    }

    [Fact]
    public void Validator_InvalidEmail_Fails()
    {
        var validator = new RegisterUserCommandValidator();
        var result = validator.Validate(new RegisterUserCommand("not-an-email", "Test User", "Password1!"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShortPassword_Fails()
    {
        var validator = new RegisterUserCommandValidator();
        var result = validator.Validate(new RegisterUserCommand("test@example.com", "Test User", "short"));
        result.IsValid.Should().BeFalse();
    }
}
