namespace ColdHarbour.Domain.Identity;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public PasswordHash PasswordHash { get; private set; } = default!;
    public Role Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public static User Create(string email, string name, PasswordHash passwordHash, Role role = Role.User)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("Email must not be empty and must contain '@'.", nameof(email));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(passwordHash);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool HasRole(Role role) => Role == role;
}
