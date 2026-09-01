using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");
        builder.HasKey(c => c.Id);

        // The same rule an album's identity follows: one shelf, one row,
        // however many machines have been told about it.
        builder.HasIndex(c => c.PublicId).IsUnique();

        // And the same tombstone rule. A removed collection is out of every
        // query in the app.
        builder.HasQueryFilter(c => c.DeletedUtc == null);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(128);

        // No two shelves may carry the same word, so the band never shows one
        // name twice and nobody has to work out which of them they meant. Over
        // live rows only, the way a person's name is: a name given back by a
        // removal is free to use again, because a tombstone is a record of what
        // happened rather than a reservation. Filtered rather than a composite
        // over the date, which would enforce nothing at all - SQLite counts
        // every NULL as different from every other one, so two live rows with
        // one name would both fit.
        builder.HasIndex(c => c.Name).IsUnique().HasFilter("\"DeletedUtc\" IS NULL");
    }
}
