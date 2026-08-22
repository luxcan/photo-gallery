using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class FaceAssignmentConfiguration : IEntityTypeConfiguration<FaceAssignment>
{
    public void Configure(EntityTypeBuilder<FaceAssignment> builder)
    {
        builder.ToTable("FaceAssignments");
        builder.HasKey(a => a.Id);

        builder.HasOne(a => a.Face)
            .WithMany()
            .HasForeignKey(a => a.FaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Person)
            .WithMany()
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // A face is linked to a given person at most once, so a rejection cannot
        // later be shadowed by a fresh proposal for the same pair.
        builder.HasIndex(a => new { a.FaceId, a.PersonId }).IsUnique();
        builder.HasIndex(a => new { a.PersonId, a.Source });
    }
}
