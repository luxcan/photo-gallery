using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class DuplicateSetConfiguration : IEntityTypeConfiguration<DuplicateSet>
{
    public void Configure(EntityTypeBuilder<DuplicateSet> builder)
    {
        builder.ToTable("DuplicateSets");
        builder.HasKey(s => s.Id);

        builder.HasMany(s => s.Members)
            .WithOne(m => m.DuplicateSet!)
            .HasForeignKey(m => m.DuplicateSetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.Kind, s.IsResolved });

        builder.Ignore(s => s.RedundantBytes);
    }
}
