using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Persistence.Configurations;

public sealed class FaceEyesConfiguration : IEntityTypeConfiguration<FaceEyes>
{
    public void Configure(EntityTypeBuilder<FaceEyes> builder)
    {
        builder.ToTable("FaceEyes");
        builder.HasKey(eyes => eyes.FaceId);

        // Dropped with its face: the face pass replaces face rows, and a
        // measurement left behind would describe a box that is no longer there.
        builder.HasOne(eyes => eyes.Face)
            .WithOne()
            .HasForeignKey<FaceEyes>(eyes => eyes.FaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
