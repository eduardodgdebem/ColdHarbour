using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Persistence;

public class ColdHarbourDbContext : DbContext
{
    public ColdHarbourDbContext(DbContextOptions<ColdHarbourDbContext> options) : base(options) { }
}
