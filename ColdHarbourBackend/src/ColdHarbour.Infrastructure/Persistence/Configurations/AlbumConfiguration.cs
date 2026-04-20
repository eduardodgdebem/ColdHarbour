using ColdHarbour.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColdHarbour.Infrastructure.Persistence.Configurations;

public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.Year)
            .IsRequired(false);

        builder.Property(a => a.CoverPath)
            .IsRequired(false);

        builder.HasOne<Artist>()
            .WithMany()
            .HasForeignKey(a => a.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new
            {
                Id = Guid.Parse("22222222-0000-0000-0000-000000000001"),
                Title = "HONNE",
                ArtistId = Guid.Parse("11111111-0000-0000-0000-000000000001"),
                Year = (int?)null,
                CoverPath = (string?)null
            },
            new
            {
                Id = Guid.Parse("22222222-0000-0000-0000-000000000002"),
                Title = "Remi Wolf",
                ArtistId = Guid.Parse("11111111-0000-0000-0000-000000000002"),
                Year = (int?)null,
                CoverPath = (string?)null
            }
        );
    }
}
