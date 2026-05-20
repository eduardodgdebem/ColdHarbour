using ColdHarbour.Domain.Identity;
using ColdHarbour.Infrastructure.Identity;
using ColdHarbour.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ColdHarbour.Infrastructure.Tests.Identity;

public class RepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ColdHarbourDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new ColdHarbourDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static User MakeUser(string email = "alice@example.com")
        => User.Create(email, "Alice", PasswordHash.From("$argon2id$v=19$m=65536,t=3,p=4$fakehash"));

    // ──────────────────────────── UserRepository ────────────────────────────

    [Fact]
    public async Task UserRepository_CanSaveAndFindByEmail()
    {
        var user = MakeUser("find-by-email@example.com");

        await using (var ctx = CreateContext())
        {
            var repo = new UserRepository(ctx);
            await repo.AddAsync(user);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new UserRepository(ctx);
            var found = await repo.FindByEmailAsync("find-by-email@example.com");

            found.Should().NotBeNull();
            found!.Name.Should().Be("Alice");
            found.Role.Should().Be(Role.User);
        }
    }

    [Fact]
    public async Task UserRepository_AnyUsersExistAsync_ReturnsFalse_WhenEmpty()
    {
        // Fresh container — no users seeded in the Identity migration
        await using var ctx = CreateContext();
        var repo = new UserRepository(ctx);

        var exists = await repo.AnyUsersExistAsync();

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UserRepository_AnyUsersExistAsync_ReturnsTrue_AfterInsert()
    {
        await using (var ctx = CreateContext())
        {
            var repo = new UserRepository(ctx);
            await repo.AddAsync(MakeUser("any@example.com"));
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new UserRepository(ctx);
            var exists = await repo.AnyUsersExistAsync();
            exists.Should().BeTrue();
        }
    }

    [Fact]
    public async Task UserRepository_FindByIdAsync_ReturnsUser()
    {
        var user = MakeUser("by-id@example.com");

        await using (var ctx = CreateContext())
        {
            var repo = new UserRepository(ctx);
            await repo.AddAsync(user);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new UserRepository(ctx);
            var found = await repo.FindByIdAsync(user.Id);
            found.Should().NotBeNull();
            found!.Email.Should().Be("by-id@example.com");
        }
    }

    // ──────────────────────────── RefreshTokenRepository ────────────────────

    private static RefreshToken MakeToken(Guid userId, string hash = "abc123")
        => RefreshToken.Create(userId, hash, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));

    [Fact]
    public async Task RefreshTokenRepository_CanSaveAndFindByTokenHash()
    {
        var user = MakeUser("refresh@example.com");
        var token = MakeToken(user.Id, "deadbeef01234567deadbeef01234567deadbeef01234567deadbeef01234567");

        await using (var ctx = CreateContext())
        {
            ctx.Users.Add(user);
            var repo = new RefreshTokenRepository(ctx);
            await repo.AddAsync(token);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new RefreshTokenRepository(ctx);
            var found = await repo.FindByTokenHashAsync(token.TokenHash);

            found.Should().NotBeNull();
            found!.UserId.Should().Be(user.Id);
        }
    }

    [Fact]
    public async Task RefreshTokenRepository_DeleteExpiredAndRevokedAsync_RemovesExpiredTokens()
    {
        var user = MakeUser("sweep-expired@example.com");
        var valid = RefreshToken.Create(user.Id, "valid" + new string('0', 59), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));
        var expired = RefreshToken.Create(user.Id, "exp00" + new string('0', 59), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1));

        await using (var ctx = CreateContext())
        {
            ctx.Users.Add(user);
            ctx.RefreshTokens.AddRange(valid, expired);
            await ctx.SaveChangesAsync();
            // Back-date expiry directly so we bypass Create's validation
            await ctx.Database.ExecuteSqlRawAsync(
                $"UPDATE \"RefreshTokens\" SET \"ExpiresAt\" = NOW() - INTERVAL '1 day' WHERE \"Id\" = '{expired.Id}'");
        }

        await using (var ctx = CreateContext())
        {
            var repo = new RefreshTokenRepository(ctx);
            await repo.DeleteExpiredAndRevokedAsync();
        }

        await using (var ctx = CreateContext())
        {
            var remaining = await ctx.RefreshTokens.ToListAsync();
            remaining.Should().ContainSingle(t => t.Id == valid.Id);
            remaining.Should().NotContain(t => t.Id == expired.Id);
        }
    }

    [Fact]
    public async Task RefreshTokenRepository_DeleteExpiredAndRevokedAsync_RemovesRevokedTokens()
    {
        var user = MakeUser("sweep-revoked@example.com");
        var valid = RefreshToken.Create(user.Id, "ok000" + new string('0', 59), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));
        var revoked = RefreshToken.Create(user.Id, "rev00" + new string('0', 59), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));
        revoked.Revoke();

        await using (var ctx = CreateContext())
        {
            ctx.Users.Add(user);
            ctx.RefreshTokens.AddRange(valid, revoked);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new RefreshTokenRepository(ctx);
            await repo.DeleteExpiredAndRevokedAsync();
        }

        await using (var ctx = CreateContext())
        {
            var remaining = await ctx.RefreshTokens.ToListAsync();
            remaining.Should().ContainSingle(t => t.Id == valid.Id);
            remaining.Should().NotContain(t => t.Id == revoked.Id);
        }
    }

    [Fact]
    public async Task RefreshTokenRepository_RevokeFamilyAsync_RevokesAllFamilyMembers()
    {
        var user = MakeUser("family@example.com");
        var familyId = Guid.NewGuid();

        var t1 = RefreshToken.Create(user.Id, "aaaa" + new string('0', 60), familyId, DateTimeOffset.UtcNow.AddDays(14));
        var t2 = RefreshToken.Create(user.Id, "bbbb" + new string('0', 60), familyId, DateTimeOffset.UtcNow.AddDays(14));
        var t3 = RefreshToken.Create(user.Id, "cccc" + new string('0', 60), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(14));

        await using (var ctx = CreateContext())
        {
            ctx.Users.Add(user);
            ctx.RefreshTokens.AddRange(t1, t2, t3);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new RefreshTokenRepository(ctx);
            await repo.RevokeFamilyAsync(familyId);
        }

        await using (var ctx = CreateContext())
        {
            var tokens = await ctx.RefreshTokens.ToListAsync();

            tokens.Where(t => t.FamilyId == familyId)
                .Should().AllSatisfy(t => t.IsConsumed.Should().BeTrue("family members must be revoked"));

            tokens.First(t => t.Id == t3.Id)
                .IsConsumed.Should().BeFalse("token from different family must not be touched");
        }
    }
}
