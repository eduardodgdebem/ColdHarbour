using ColdHarbour.Infrastructure.Identity;
using FluentAssertions;

namespace ColdHarbour.Infrastructure.Tests.Identity;

public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void Hash_ReturnsDifferentHashForSamePlaintext()
    {
        var hash1 = _sut.Hash("password123");
        var hash2 = _sut.Hash("password123");

        hash1.Should().NotBe(hash2, "Argon2 must include a random salt so identical plaintexts produce different hashes");
    }

    [Fact]
    public void Verify_ReturnsTrueForMatchingPlaintext()
    {
        var plaintext = "correct-horse-battery-staple";
        var hash = _sut.Hash(plaintext);

        _sut.Verify(plaintext, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalseForWrongPlaintext()
    {
        var hash = _sut.Hash("the-real-password");

        _sut.Verify("not-the-real-password", hash).Should().BeFalse();
    }
}
