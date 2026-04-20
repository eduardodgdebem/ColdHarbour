using ColdHarbour.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColdHarbour.Infrastructure.Persistence.Configurations;

public class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.Provider)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(t => t.ProviderRef)
            .IsRequired(false);

        builder.Property(t => t.LocalPath)
            .IsRequired(false);

        builder.Property(t => t.Format)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(t => t.AudioSha1)
            .IsRequired()
            .HasColumnType("char(40)");

        builder.Property(t => t.Bitrate)
            .IsRequired();

        // Store Duration as long (ticks) since PostgreSQL doesn't have a native ticks type.
        builder.Property(t => t.Duration)
            .HasConversion(
                duration => duration.Ticks,
                ticks => TimeSpan.FromTicks(ticks));

        builder.HasOne<Album>()
            .WithMany()
            .HasForeignKey(t => t.AlbumId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
