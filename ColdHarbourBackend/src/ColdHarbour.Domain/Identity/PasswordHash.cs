namespace ColdHarbour.Domain.Identity;

public sealed record PasswordHash(string Value)
{
    public static PasswordHash From(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Hash must not be empty.", nameof(hash));
        return new PasswordHash(hash);
    }
}
