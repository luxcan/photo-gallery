using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class CollectionRulePlaceConfiguration
    : IEntityTypeConfiguration<CollectionRulePlace>
{
    public void Configure(EntityTypeBuilder<CollectionRulePlace> builder)
    {
        builder.ToTable("CollectionRulePlaces");
        builder.HasKey(rule => new { rule.CollectionId, rule.PlaceId });

        builder.HasOne(rule => rule.Place)
            .WithMany()
            .HasForeignKey(rule => rule.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rule => rule.PlaceId);
    }
}
