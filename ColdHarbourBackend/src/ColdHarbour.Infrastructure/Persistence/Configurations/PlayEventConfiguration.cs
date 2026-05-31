using ColdHarbour.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColdHarbour.Infrastructure.Persistence.Configurations;

public sealed class PlayEventConfiguration : IEntityTypeConfiguration<PlayEvent>
{
    public void Configure(EntityTypeBuilder<PlayEvent> b)
    {
        b.ToTable("PlayEvents");
        b.HasKey(e => e.Id);
        b.Property(e => e.UserId).IsRequired();
        b.Property(e => e.DeviceId).IsRequired();
        b.Property(e => e.TrackId).IsRequired();
        b.Property(e => e.StartedAt).IsRequired();
        b.Property(e => e.EndedAt);
        b.Property(e => e.CompletedRatio);
        // Phase 3: pause-aware listened time
        b.Property(e => e.ListenedMs).IsRequired().HasDefaultValue(0L);
        b.Property(e => e.PausedAtUtc);
        b.Property(e => e.SegmentStartedAt).IsRequired();
        b.HasIndex(e => e.UserId);
        b.HasIndex(e => new { e.UserId, e.EndedAt });
    }
}
