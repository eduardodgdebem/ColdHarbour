using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Identity;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ColdHarbourDbContext _db;

    public RefreshTokenRepository(ColdHarbourDbContext db) => _db = db;

    public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
        => await _db.RefreshTokens.AddAsync(token, ct);

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null && t.ReplacedById == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.Revoke();

        await _db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public Task DeleteExpiredAndRevokedAsync(CancellationToken ct = default)
        => _db.RefreshTokens
            .Where(t => t.ExpiresAt <= DateTimeOffset.UtcNow || t.RevokedAt != null)
            .ExecuteDeleteAsync(ct);
}
