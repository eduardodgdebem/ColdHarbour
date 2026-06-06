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
        // Honor the same env var the app and the Makefile use, so `dotnet ef`
        // talks to the real configured database instead of a baked-in default.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=coldharbourdb;Username=user;Password=password";

        var options = new DbContextOptionsBuilder<ColdHarbourDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ColdHarbourDbContext(options);
    }
}
