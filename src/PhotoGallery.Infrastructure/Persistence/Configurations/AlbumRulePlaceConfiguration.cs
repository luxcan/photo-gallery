using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class AlbumRulePlaceConfiguration
    : IEntityTypeConfiguration<AlbumRulePlace>
{
    public void Configure(EntityTypeBuilder<AlbumRulePlace> builder)
    {
        builder.ToTable("AlbumRulePlaces");
        builder.HasKey(rule => new { rule.AlbumId, rule.PlaceId });

        builder.HasOne(rule => rule.Place)
            .WithMany()
            .HasForeignKey(rule => rule.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rule => rule.PlaceId);
    }
}
