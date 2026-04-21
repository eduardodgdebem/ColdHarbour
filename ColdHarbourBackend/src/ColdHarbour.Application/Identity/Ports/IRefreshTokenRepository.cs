using ColdHarbour.Domain.Identity;

namespace ColdHarbour.Application.Identity.Ports;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
