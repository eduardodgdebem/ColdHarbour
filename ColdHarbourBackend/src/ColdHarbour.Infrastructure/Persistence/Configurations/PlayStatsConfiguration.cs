using ColdHarbour.Infrastructure.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColdHarbour.Infrastructure.Persistence.Configurations;

public sealed class PlayStatsConfiguration : IEntityTypeConfiguration<PlayStats>
{
    public void Configure(EntityTypeBuilder<PlayStats> b)
    {
        b.ToTable("PlayStats");
        b.HasKey(s => new { s.TrackId, s.WeekOf });
        b.Property(s => s.TrackId).IsRequired();
        b.Property(s => s.WeekOf).IsRequired();
        b.Property(s => s.PlayCount).IsRequired();
        b.Property(s => s.TotalMs).IsRequired();
    }
}
