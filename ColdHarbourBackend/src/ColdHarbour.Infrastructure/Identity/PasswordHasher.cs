using ColdHarbour.Application.Identity.Ports;
using Isopoh.Cryptography.Argon2;

namespace ColdHarbour.Infrastructure.Identity;

public sealed class PasswordHasher : IPasswordHasher
{
    public string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return Argon2.Hash(plaintext);
    }

    public bool Verify(string plaintext, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        return Argon2.Verify(hash, plaintext);
    }
}
