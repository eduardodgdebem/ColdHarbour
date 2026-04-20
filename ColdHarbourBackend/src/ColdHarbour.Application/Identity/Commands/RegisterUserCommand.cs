using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using FluentValidation;
using MediatR;

namespace ColdHarbour.Application.Identity.Commands;

public sealed record RegisterUserCommand(string Email, string Name, string Password) : IRequest;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Name)
            .NotEmpty();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);
    }
}

public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public RegisterUserCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var usersExist = await _userRepository.AnyUsersExistAsync(cancellationToken);
        if (usersExist)
            throw new UnauthorizedAccessException("Registration is closed; contact the owner.");

        var hash = _passwordHasher.Hash(request.Password);
        var user = User.Create(request.Email, request.Name, PasswordHash.From(hash));

        await _userRepository.AddAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);
    }
}
