using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(128);

        // Unique among people who are still here. A tombstone's name is blanked
        // when it is made, so every tombstone shares the one empty name, and an
        // unfiltered index would refuse the second deletion - as it once refused
        // somebody being added back under a name a tombstone still held.
        builder.HasIndex(p => p.DisplayName).IsUnique().HasFilter("\"DeletedUtc\" IS NULL");

        // Unique so that a merge cannot make two rows for one person, and indexed
        // because every answer arriving from another machine is looked up by it.
        // Not filtered: a tombstone is exactly the row a merge must find.
        builder.HasIndex(p => p.PublicId).IsUnique();

        // Deleted people are gone from every query in the app without one of
        // them having to remember it. The sharing code, which is the one place
        // that wants the tombstones, asks for them with IgnoreQueryFilters.
        builder.HasQueryFilter(p => p.DeletedUtc == null);

        builder.HasMany(p => p.Eras)
            .WithOne(e => e.Person!)
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
