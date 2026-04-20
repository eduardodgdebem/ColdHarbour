using ColdHarbour.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColdHarbour.Infrastructure.Persistence.Configurations;

public class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasData(
            new { Id = Guid.Parse("11111111-0000-0000-0000-000000000001"), Name = "HONNE" },
            new { Id = Guid.Parse("11111111-0000-0000-0000-000000000002"), Name = "Remi Wolf" }
        );
    }
}
