using ColdHarbour.Domain.Identity;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Identity;

public class RefreshTokenTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly Guid ValidFamilyId = Guid.NewGuid();
    private static readonly string ValidTokenHash = new string('b', 64);
    private static readonly DateTimeOffset FutureExpiry = DateTimeOffset.UtcNow.AddDays(14);

    [Fact]
    public void Create_WithValidInputs_SetsAllProperties()
    {
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);

        token.Id.Should().NotBeEmpty();
        token.UserId.Should().Be(ValidUserId);
        token.TokenHash.Should().Be(ValidTokenHash);
        token.FamilyId.Should().Be(ValidFamilyId);
        token.ExpiresAt.Should().Be(FutureExpiry);
        token.RevokedAt.Should().BeNull();
        token.ReplacedById.Should().BeNull();
        token.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_WithEmptyUserId_Throws()
    {
        var act = () => RefreshToken.Create(Guid.Empty, ValidTokenHash, ValidFamilyId, FutureExpiry);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidTokenHash_Throws(string? hash)
    {
        var act = () => RefreshToken.Create(ValidUserId, hash!, ValidFamilyId, FutureExpiry);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptyFamilyId_Throws()
    {
        var act = () => RefreshToken.Create(ValidUserId, ValidTokenHash, Guid.Empty, FutureExpiry);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithPastExpiry_Throws()
    {
        var pastExpiry = DateTimeOffset.UtcNow.AddSeconds(-1);

        var act = () => RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, pastExpiry);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenNotExpired()
    {
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);

        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenExpired()
    {
        // We construct with a valid expiry, then test the property against a past time.
        // Since we cannot create with past expiry, we test a token that was created with
        // DateTimeOffset.UtcNow.AddSeconds(-1) would fail creation, confirming the guard.
        // Instead, verify via Rotate that expiry is honoured:
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);
        token.IsExpired.Should().BeFalse();

        // Directly verify the computed property logic by checking a future token is not expired.
        // The "ReturnsTrue_WhenExpired" case is verified by the Create_WithPastExpiry_Throws guard above
        // plus a direct property check on a known-expired wrapper:
        var expiredToken = RefreshToken.CreateExpiredForTesting(ValidUserId, ValidTokenHash, ValidFamilyId);
        expiredToken.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsConsumed_ReturnsFalse_WhenFresh()
    {
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);

        token.IsConsumed.Should().BeFalse();
    }

    [Fact]
    public void Rotate_SetsReplacedByIdOnCurrent_AndReturnsNewToken()
    {
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);
        var newId = Guid.NewGuid();
        var newHash = new string('c', 64);
        var newExpiry = DateTimeOffset.UtcNow.AddDays(14);

        var newToken = token.Rotate(newId, newHash, newExpiry);

        token.ReplacedById.Should().Be(newId);
        newToken.Id.Should().Be(newId);
        newToken.TokenHash.Should().Be(newHash);
        newToken.ExpiresAt.Should().Be(newExpiry);
    }

    [Fact]
    public void Rotate_NewTokenHasSameFamilyId()
    {
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);
        var newId = Guid.NewGuid();

        var newToken = token.Rotate(newId, new string('c', 64), DateTimeOffset.UtcNow.AddDays(14));

        newToken.FamilyId.Should().Be(ValidFamilyId);
        newToken.UserId.Should().Be(ValidUserId);
    }

    [Fact]
    public void Revoke_SetsRevokedAt()
    {
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);

        token.Revoke();

        token.RevokedAt.Should().NotBeNull();
        token.RevokedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void IsConsumed_ReturnsTrue_AfterRevoke()
    {
        var token = RefreshToken.Create(ValidUserId, ValidTokenHash, ValidFamilyId, FutureExpiry);

        token.Revoke();

        token.IsConsumed.Should().BeTrue();
    }
}
