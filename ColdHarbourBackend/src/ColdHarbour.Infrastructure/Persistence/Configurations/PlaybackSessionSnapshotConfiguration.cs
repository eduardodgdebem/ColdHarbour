using ColdHarbour.Infrastructure.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColdHarbour.Infrastructure.Persistence.Configurations;

public sealed class PlaybackSessionSnapshotConfiguration : IEntityTypeConfiguration<PlaybackSessionSnapshot>
{
    public void Configure(EntityTypeBuilder<PlaybackSessionSnapshot> b)
    {
        b.ToTable("PlaybackSessionSnapshots");
        b.HasKey(s => s.UserId);

        b.Property(s => s.UserId).IsRequired();
        b.Property(s => s.ActiveDeviceId);
        b.Property(s => s.TrackId);
        b.Property(s => s.PositionMs).IsRequired();
        b.Property(s => s.IsPlaying).IsRequired();

        // Queue is serialised as a JSON string by the store; EF maps it as jsonb text.
        b.Property(s => s.QueueJson)
            .HasColumnName("Queue")
            .HasColumnType("jsonb")
            .IsRequired();

        b.Property(s => s.QueueIndex).IsRequired();

        b.Property(s => s.RepeatMode)
            .HasMaxLength(8)
            .IsRequired();

        b.Property(s => s.Shuffle).IsRequired();
        b.Property(s => s.UpdatedAt).IsRequired();
    }
}
