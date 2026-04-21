using ColdHarbour.Application.Identity.Ports;
using ColdHarbour.Domain.Identity;
using ColdHarbour.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Identity;

public sealed class UserRepository : IUserRepository
{
    private readonly ColdHarbourDbContext _db;

    public UserRepository(ColdHarbourDbContext db) => _db = db;

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default)
        => _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Users.FindAsync([id], ct).AsTask();

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await _db.Users.AddAsync(user, ct);

    public Task<bool> AnyUsersExistAsync(CancellationToken ct = default)
        => _db.Users.AnyAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
