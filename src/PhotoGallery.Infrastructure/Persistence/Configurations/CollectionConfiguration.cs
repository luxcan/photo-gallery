using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(128);
        builder.Property(c => c.ProposalKey).HasMaxLength(24);

        builder.HasMany(c => c.Members)
            .WithOne(m => m.Collection!)
            .HasForeignKey(m => m.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.RulePeople)
            .WithOne(rule => rule.Collection!)
            .HasForeignKey(rule => rule.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.RulePlaces)
            .WithOne(rule => rule.Collection!)
            .HasForeignKey(rule => rule.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // A computed property, not a column: it reads the three parts of the
        // rule that are stored.
        builder.Ignore(c => c.HasRule);

        // How a rebuild finds the row it wrote last time instead of adding a
        // second one. Unique because two occasions cannot cover the same run of
        // days - runs are disjoint by construction.
        builder.HasIndex(c => c.ProposalKey).IsUnique();

        // The pass reads "the proposals only"; the screen reads "mine only".
        builder.HasIndex(c => c.Origin);

        // The list is read in span order every time the screen opens.
        builder.HasIndex(c => c.StartUtc);

        // Indexed but not related, following the asset's own place column: the
        // only delete behaviour in this model is cascade, and a real foreign key
        // here would take a whole collection away with its cover photograph.
        builder.HasIndex(c => c.PlaceId);
        builder.HasIndex(c => c.CoverAssetId);
    }
}
