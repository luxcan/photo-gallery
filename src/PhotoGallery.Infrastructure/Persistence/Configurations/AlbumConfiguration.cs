using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("Albums");
        builder.HasKey(c => c.Id);

        // The same rule as a person's, for the same reason: one album, one row,
        // however many machines have been told about it.
        builder.HasIndex(c => c.PublicId).IsUnique();

        // And the same tombstone rule. A deleted album is out of every query in
        // the app; only the sharing code asks to see it.
        builder.HasQueryFilter(c => c.DeletedUtc == null);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(128);
        builder.Property(c => c.ProposalKey).HasMaxLength(24);

        builder.HasMany(c => c.Members)
            .WithOne(m => m.Album!)
            .HasForeignKey(m => m.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.RulePeople)
            .WithOne(rule => rule.Album!)
            .HasForeignKey(rule => rule.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.RulePlaces)
            .WithOne(rule => rule.Album!)
            .HasForeignKey(rule => rule.AlbumId)
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
        // here would take a whole album away with its cover photograph.
        builder.HasIndex(c => c.PlaceId);
        builder.HasIndex(c => c.CoverAssetId);

        // The shelf, on the same terms: indexed but not related. A real foreign
        // key here cannot be added to an existing table by SQLite, so EF would
        // rebuild the whole Albums table to attach it - and that rebuild turns
        // off foreign keys, which cannot happen inside a transaction, so the
        // migration stops being all-or-nothing. Removing a collection already
        // clears this column itself, and the screen treats a shelf it has never
        // heard of as no shelf, so nothing is left resting on the constraint.
        builder.HasIndex(c => c.CollectionId);
    }
}
