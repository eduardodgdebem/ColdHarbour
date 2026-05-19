using ColdHarbour.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColdHarbour.Infrastructure.Persistence.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.UserAgent).IsRequired().HasMaxLength(500);
        builder.Property(d => d.PreferredProfile).IsRequired().HasMaxLength(20);

        // Store codec list as comma-separated string
        builder.Property(d => d.SupportedCodecs)
            .HasConversion(
                v => string.Join(',', v),
                v => (IReadOnlyList<string>)v.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .HasMaxLength(500);

        builder.HasIndex(d => d.UserId);
    }
}
