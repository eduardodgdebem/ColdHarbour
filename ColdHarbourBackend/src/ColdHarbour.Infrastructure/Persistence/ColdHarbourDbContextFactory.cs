using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ColdHarbour.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core tooling (dotnet ef migrations add, dotnet ef database update).
/// Not registered in DI; not used at runtime.
/// </summary>
public class ColdHarbourDbContextFactory : IDesignTimeDbContextFactory<ColdHarbourDbContext>
{
    public ColdHarbourDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=coldharbourdb;Username=user;Password=password")
            .Options;

        return new ColdHarbourDbContext(options);
    }
}
