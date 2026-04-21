using ColdHarbour.Domain.Identity;

namespace ColdHarbour.Application.Identity.Ports;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task<bool> AnyUsersExistAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
