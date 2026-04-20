using ColdHarbour.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Persistence;

public class ColdHarbourDbContext : DbContext
{
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();

    public ColdHarbourDbContext(DbContextOptions<ColdHarbourDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ColdHarbourDbContext).Assembly);
}
