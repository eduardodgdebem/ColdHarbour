using ColdHarbour.Domain.Identity;
using ColdHarbour.Domain.Library;
using ColdHarbour.Domain.Playback;
using ColdHarbour.Infrastructure.Playback;
using Microsoft.EntityFrameworkCore;

namespace ColdHarbour.Infrastructure.Persistence;

public class ColdHarbourDbContext : DbContext
{
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PlayEvent> PlayEvents => Set<PlayEvent>();
    public DbSet<PlayStats> PlayStats => Set<PlayStats>();
    public DbSet<PlaybackSessionSnapshot> PlaybackSessionSnapshots => Set<PlaybackSessionSnapshot>();

    public ColdHarbourDbContext(DbContextOptions<ColdHarbourDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ColdHarbourDbContext).Assembly);
}
