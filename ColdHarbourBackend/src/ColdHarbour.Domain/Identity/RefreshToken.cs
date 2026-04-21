namespace ColdHarbour.Domain.Identity;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsConsumed => ReplacedById.HasValue || RevokedAt.HasValue;

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string tokenHash, Guid familyId, DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("TokenHash must not be empty.", nameof(tokenHash));
        if (familyId == Guid.Empty)
            throw new ArgumentException("FamilyId must not be empty.", nameof(familyId));
        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("ExpiresAt must be in the future.", nameof(expiresAt));

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a token with a past expiry. Exposed to the test assembly only.
    /// </summary>
    internal static RefreshToken CreateExpiredForTesting(Guid userId, string tokenHash, Guid familyId)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-15)
        };
    }

    public RefreshToken Rotate(Guid newTokenId, string newTokenHash, DateTimeOffset newExpiresAt)
    {
        ReplacedById = newTokenId;

        return new RefreshToken
        {
            Id = newTokenId,
            UserId = UserId,
            TokenHash = newTokenHash,
            FamilyId = FamilyId,
            ExpiresAt = newExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Revoke()
    {
        RevokedAt = DateTimeOffset.UtcNow;
    }
}
